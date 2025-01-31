#region license
/*
Copyright 2005 - 2021 Advantage Solutions, s. r. o.

This file is part of ORIGAM (http://www.origam.org).

ORIGAM is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

ORIGAM is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with ORIGAM. If not, see <http://www.gnu.org/licenses/>.
*/
#endregion

using System;
using System.Data;
using System.Xml;
using System.Xml.XPath;
using System.Collections;
using System.Timers;

using Origam.DA;
using Origam.DA.Service;
using Origam.Rule;
using Origam.Schema.EntityModel;
using Origam.Schema;
using Origam.Schema.WorkflowModel;
using Origam.Workbench.Services;
using core = Origam.Workbench.Services.CoreServices;
using System.ComponentModel;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using Origam.Services;
using System.Transactions;
using Origam.Extensions;
using Origam.Rule.Xslt;
using Origam.Service.Core;
using Timer = System.Timers.Timer;

namespace Origam.Workflow.WorkQueue;
/// <summary>
/// Summary description for WorkQueueService.
/// </summary>
public class WorkQueueService : IWorkQueueService, IBackgroundService
{
    private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
    private const string WQ_EVENT_ONCREATE = "fe40902f-8a44-477e-96f9-d157eee16a0f";
    private readonly core.ICoreDataService dataService = core.DataService.Instance;
    private CancellationTokenSource cancellationTokenSource = new ();
    private readonly WorkQueueUtils workQueueUtils;
    private readonly IWorkQueueProcessor queueProcessor;
    private readonly WorkQueueThrottle workQueueThrottle;
    private readonly Timer _loadExternalWorkQueuesTimer = new (60000);
    private readonly Timer _queueAutoProcessTimer;
    private Boolean serviceBeingUnloaded = false;
    private readonly RetryManager retryManager = new ();
    public WorkQueueService(): this(10_000)
    {
    }   
    public WorkQueueService(int queueProcessIntervalMillis)
    {
        _queueAutoProcessTimer = new Timer(queueProcessIntervalMillis);
        SchemaService schemaService = ServiceManager.Services.GetService(typeof(SchemaService)) as SchemaService;
        IDataLookupService dataLookupService = ServiceManager.Services
            .GetService<IDataLookupService>();
        IPersistenceService persistenceService = ServiceManager.Services
            .GetService<IPersistenceService>();
        workQueueThrottle = new WorkQueueThrottle(persistenceService);
        workQueueUtils = new WorkQueueUtils(dataLookupService, schemaService);
        schemaService.SchemaLoaded += schemaService_SchemaLoaded;
        schemaService.SchemaUnloaded += new EventHandler(schemaService_SchemaUnloaded);
        schemaService.SchemaUnloading += new CancelEventHandler(schemaService_SchemaUnloading);
        
        OrigamSettings settings 
            = ConfigurationManager.GetActiveConfiguration();
        queueProcessor = settings.WorkQueueProcessingMode switch
        {
            WorkQueueProcessingMode.Linear => 
                new LinearProcessor(
                    ProcessQueueItem,
                    workQueueUtils,
                    retryManager,
                    workQueueThrottle),
            WorkQueueProcessingMode.RoundRobin => 
                new RoundRobinLinearProcessor(
                    ProcessQueueItem,
                    workQueueUtils,
                    retryManager,
                    workQueueThrottle, 
                    settings.RoundRobinBatchSize),
            _ => 
                throw new NotImplementedException(
                $"Option {settings.WorkQueueProcessingMode} not implemented")
        };
    }
    #region IWorkbenchService Members
    public void UnloadService()
    {
        SchemaService schemaService = ServiceManager.Services.GetService<SchemaService>();
        schemaService.SchemaLoaded -= schemaService_SchemaLoaded;
        schemaService.SchemaUnloaded -= schemaService_SchemaUnloaded;
        schemaService.SchemaUnloading -= schemaService_SchemaUnloading;
        StopTasks();
    }
    public void StopTasks()
    {
        if(log.IsDebugEnabled)
        {
            log.DebugFormat("Stopping WorkQueueService Timers");
        }
        serviceBeingUnloaded = true;
        cancellationTokenSource.Cancel();
        // unsubscribe from 'Elapsed' events
        _loadExternalWorkQueuesTimer.Elapsed -= new ElapsedEventHandler(LoadExternalWorkQueuesElapsed);
        _queueAutoProcessTimer.Elapsed -= new ElapsedEventHandler(WorkQueueAutoProcessTimerElapsed);
        // stop timers
        _queueAutoProcessTimer.Stop();
        _loadExternalWorkQueuesTimer.Stop();
        while (_queueAutoProcessBusy || _externalQueueAdapterBusy)
        {
            if (log.IsInfoEnabled)
            {
                log.Info("Unloading service - waiting for queues to finish.");
            }
            System.Threading.Thread.Sleep(1000);
        }
        serviceBeingUnloaded = false;
        cancellationTokenSource = new();
    }
    public void InitializeService()
    {
        serviceBeingUnloaded = false;
    }
    #endregion
    #region IWorkQueueService Members
    public DataSet UserQueueList()
    {
        // Load all active work queues
        DataSet result = dataService.LoadData(new Guid("3a23f4e1-368c-4163-a790-4eed173af83d"), new Guid("ed3d93ca-bd4e-4830-8d26-f7120c8fc7ff"), Guid.Empty, Guid.Empty, null);
        // filter out those current user has no access to
        var rowsToDelete = new List<DataRow>();
        foreach (DataRow row in result.Tables["WorkQueue"].Rows)
        {
            IOrigamAuthorizationProvider auth = SecurityManager.GetAuthorizationProvider();
            if (row.IsNull("Roles") || !auth.Authorize(SecurityManager.CurrentPrincipal, (string)row["Roles"]))
            {
                rowsToDelete.Add(row);
            }
        }
        foreach (DataRow row in rowsToDelete)
        {
            row.Delete();
        }
        result.AcceptChanges();
        return result;
    }
    public ISchemaItem WQClass(string name)
    {
        return workQueueUtils.WorkQueueClass(name);
    }
     public ISchemaItem WQClass(Guid queueId)
     {
         return workQueueUtils.WorkQueueClass(queueId);
     }
     public DataSet LoadWorkQueueData(string workQueueClass, object queueId)
    {
        return workQueueUtils.LoadWorkQueueData(workQueueClass, queueId, 0, 0, null);
    }
    public Guid WorkQueueAdd(string workQueueName, IXmlContainer data, string transactionId)
    {
        Guid workQueueId = workQueueUtils.GetQueueId(workQueueName);
        string workQueueClass = workQueueUtils.WorkQueueClassName(workQueueId);
        string condition = "";
        return WorkQueueAdd(workQueueClass, workQueueName, workQueueId, condition, data, null, transactionId);
    }
    public Guid WorkQueueAdd(string workQueueName, IXmlContainer data, WorkQueueAttachment[] attachments, string transactionId)
    {
        Guid workQueueId = workQueueUtils.GetQueueId(workQueueName);
        string workQueueClass = workQueueUtils.WorkQueueClassName(workQueueId);
        string condition = "";
        return WorkQueueAdd(workQueueClass, workQueueName, workQueueId, condition, data, attachments, transactionId);
    }
    public Guid WorkQueueAdd(string workQueueClass, string workQueueName, Guid workQueueId, string condition, IXmlContainer data, string transactionId)
    {
        return WorkQueueAdd(workQueueClass, workQueueName, workQueueId, condition, data, null, transactionId);
    }
    public Guid WorkQueueAdd(string workQueueClass, string workQueueName, Guid workQueueId, string condition, IXmlContainer data, WorkQueueAttachment[] attachments, string transactionId)
    {
        if (log.IsDebugEnabled)
        {
            log.Debug("Adding Work Queue Entry for Queue: " + workQueueName);
        }
        RuleEngine ruleEngine = RuleEngine.Create(new Hashtable(), transactionId);
        UserProfile profile = SecurityManager.CurrentUserProfile();
        WorkQueueClass wqc = workQueueUtils.WorkQueueClass(workQueueClass);
        if (wqc != null)
        {
            Guid rowId = Guid.NewGuid();
            DataSet ds = new DatasetGenerator(true).CreateDataSet(wqc.WorkQueueStructure);
            DataTable table = ds.Tables[0];
            DataRow row = table.NewRow();
            row["Id"] = rowId;
            row["refWorkQueueId"] = workQueueId;
            row["RecordCreated"] = DateTime.Now;
            row["RecordCreatedBy"] = profile.Id;
            WorkQueueRowFill(wqc, ruleEngine, row, data);
            table.Rows.Add(row);
            if (condition != null && condition != string.Empty)
            {
                if (!EvaluateWorkQueueCondition(row, condition, workQueueName, transactionId))
                {
                    return Guid.Empty;
                }
            }
            StoreQueueItems(wqc, table, transactionId);
            // add attachments
            if (attachments != null)
            {
                AttachmentService attsvc = ServiceManager.Services.GetService(typeof(AttachmentService)) as AttachmentService;
                foreach (WorkQueueAttachment att in attachments)
                {
                    attsvc.AddAttachment(att.Name, att.Data, rowId, profile.Id, transactionId);
                }
            }
            // notifications - OnCreate
            ProcessNotifications(wqc, workQueueId, new Guid(WQ_EVENT_ONCREATE), ds, transactionId);
            return (Guid)row["Id"];
        }
        return Guid.Empty;
    }
    public IDataDocument WorkQueueGetMessage(Guid workQueueMessageId, string transactionId)
    {
        WorkQueueClass wqc = workQueueUtils.WorkQueueClassByMessageId(workQueueMessageId);
        DataSet ds = FetchSingleQueueEntry(wqc, workQueueMessageId, transactionId);
        return DataDocumentFactory.New(ds);
    }
    private void ProcessNotifications(WorkQueueClass wqc, Guid workQueueId, Guid eventTypeId, DataSet queueItem, string transactionId)
    {
        IPersistenceService persistence = ServiceManager.Services.GetService(typeof(IPersistenceService)) as IPersistenceService;
        WorkQueueData wq = GetQueue(workQueueId);
        foreach (WorkQueueData.WorkQueueNotificationRow notification in wq.WorkQueueNotification.Rows)
        {
            if (log.IsDebugEnabled)
            {
                log.Debug("Testing notification " + notification?.Description);
            }
            // check if the event type is equal (OnCreate, OnEscalate, etc...)
            if (!notification.refWorkQueueNotificationEventId.Equals(eventTypeId))
            {
                if (log.IsDebugEnabled)
                {
                    log.Debug("Wrong event type. Notification will not be sent.");
                }
                continue;
            }
            // notification source
            IXmlContainer notificationSource;
            if (wqc.NotificationStructure == null)
            {
                if (log.IsDebugEnabled)
                {
                    log.Debug("Notification source is work queue item.");
                }
                notificationSource = DataDocumentFactory.New(queueItem);
            }
            else
            {
                if (log.IsDebugEnabled)
                {
                    log.Debug("Notification source is " + wqc.NotificationStructure.Path);
                }
                DataSet dataSet = dataService.LoadData(
                    dataStructureId: wqc.NotificationStructureId, 
                    methodId: wqc.NotificationLoadMethodId,
                    defaultSetId: Guid.Empty,
                    sortSetId: Guid.Empty, 
                    transactionId: transactionId, 
                    paramName1: wqc.NotificationFilterPkParameter, 
                    paramValue1: queueItem.Tables[0].Rows[0]["refId"]);
                notificationSource = DataDocumentFactory.New(dataSet);
                if (log.IsDebugEnabled)
                {
                    log.Debug("Notification source result: " + notificationSource?.Xml?.OuterXml);
                }
            }
            DataRow workQueueRow = ExtractWorkQueueRowIfNotNotificationDatastructureIsSet(wqc, queueItem);
            // senders
            Hashtable senders = new Hashtable();
            // evaluate senders - get one for each channel (e-mail, sms...) and then assign them by each recipient's notification channel
            foreach (WorkQueueData.WorkQueueNotificationContact_SendersRow sender in notification.GetWorkQueueNotificationContact_SendersRows())
            {
                OrigamNotificationContactData senderData = GetNotificationContacts(
                        workQueueNotificationContactTypeId: sender.refWorkQueueNotificationContactTypeId,
                        origamNotificationChannelTypeId: sender.refOrigamNotificationChannelTypeId,
                        value: sender.Value,
                        context: notificationSource,
                        workQueueRow: workQueueRow,
                        transactionId: transactionId);
                if (senderData.OrigamNotificationContact.Count == 0 || senderData.OrigamNotificationContact[0].ContactIdentification == "")
                {
                    if (log.IsErrorEnabled)
                    {
                        log.Error(string.Format("Skipping notification for workqueue notification sender definition {0}, no sender returned", sender?.Id));
                    }
                    continue;
                }
                senders[sender.refOrigamNotificationChannelTypeId] = senderData.OrigamNotificationContact[0].ContactIdentification;
            }
            // recipients
            WorkQueueData.WorkQueueNotificationContact_RecipientsRow[] recipientRows = notification.GetWorkQueueNotificationContact_RecipientsRows();
            if (log.IsDebugEnabled)
            {
                log.Debug("Number of recipients rows defined for workqueue notification: " + recipientRows.Length.ToString());
            }
            // evaluate recipient rows and send a notifications. For each row there can be found out more than one recipient.
            foreach (WorkQueueData.WorkQueueNotificationContact_RecipientsRow recipientRow in recipientRows)
            {
                string value = (recipientRow.IsValueNull() ? null : recipientRow.Value);
                OrigamNotificationContactData recipients = GetNotificationContacts(
                        workQueueNotificationContactTypeId: recipientRow.refWorkQueueNotificationContactTypeId,
                        origamNotificationChannelTypeId: recipientRow.refOrigamNotificationChannelTypeId,
                        value: value,
                        context: notificationSource,
                        workQueueRow: workQueueRow,
                        transactionId: transactionId);
                if (recipients == null || recipients.OrigamNotificationContact.Count == 0)
                {
                    // continue to process next recipient definition row
                    if (log.IsDebugEnabled)
                    {
                        log.Debug("Didn't get any response when trying to get recipients.");
                    }
                    continue;
                }
                if (log.IsDebugEnabled)
                {
                    log.Debug("Recipients: " + recipients?.GetXml());
                }
                if (!senders.Contains(recipientRow.refOrigamNotificationChannelTypeId))
                {
                    if (log.IsDebugEnabled)
                    {
                        log.Debug(String.Format("Can't find any sender for notification channel `{0}'", recipientRow.refOrigamNotificationChannelTypeId));
                    }
                    // continue to process next recipient definition row
                    continue;
                }
                // for each recipient generate and send the mail
                foreach (OrigamNotificationContactData.OrigamNotificationContactRow recipient in recipients.OrigamNotificationContact.Rows)
                {
                    // generate data for mail
                    IDataDocument notificationData = GenerateNotificationMessage(
                            notification.refOrigamNotificationTemplateId,
                            notificationSource,
                            recipient,
                            workQueueRow,
                            transactionId);
                    // processing data for mail (output from transformation)
                    if (notificationData.DataSet.Tables[0].Rows.Count != 1)
                    {
                        if (log.IsDebugEnabled)
                        {
                            log.Debug("Notification transformation result count: " + notificationData.DataSet.Tables[0].Rows.Count.ToString());
                        }
                        continue;
                    }
                    DataRow notificationRow = notificationData.DataSet.Tables[0].Rows[0];
                    string notificationBody = null;
                    string notificationSubject = null;
                    if (!notificationRow.IsNull("Body"))
                    {
                        notificationBody = (string)notificationRow["Body"];
                    }
                    if (!notificationRow.IsNull("Subject"))
                    {
                        notificationSubject = (string)notificationRow["Subject"];
                    }
                    if (notificationBody == null || notificationBody == ""
                        || notificationSubject == null || notificationSubject == "")
                    {
                        if (log.IsDebugEnabled)
                        {
                            log.Debug("Notification body or subject is empty. No notification will be sent.");
                        }
                        continue;
                    }
                    if (log.IsDebugEnabled)
                    {
                        log.Debug("Notification subject: " + notificationSubject);
                        log.Debug(String.Format("Notification body: `{0}'", notificationBody));
                    }
                    // send the notification - start the notification workflow
                    QueryParameterCollection pms = new QueryParameterCollection();
                    pms.Add(new QueryParameter("sender", (string)senders[recipientRow.refOrigamNotificationChannelTypeId]));
                    pms.Add(new QueryParameter("recipients", recipient.ContactIdentification));
                    pms.Add(new QueryParameter("body", notificationBody));
                    pms.Add(new QueryParameter("subject", notificationSubject));
                    pms.Add(new QueryParameter("notificationChannelTypeId", recipientRow.refOrigamNotificationChannelTypeId));
                    if (notification.SendAttachments == true)
                    {
                        pms.Add(new QueryParameter("attachmentRecordId", (Guid)queueItem.Tables[0].Rows[0]["Id"]));
                    }
                    //log.Debug(string.Format("Skipping sending of mail to {0}.", recipient.Email));
                    core.WorkflowService.ExecuteWorkflow(new Guid("0fea481a-24ab-4e98-8793-617ab5bb7272"), pms, transactionId);
                } // foreach recipient
            } // foreach definition recipient row                              
        } // foreach workqueue notification
    }
    private static DataRow ExtractWorkQueueRowIfNotNotificationDatastructureIsSet(WorkQueueClass wqc, DataSet queueItem)
    {
        // store WorkQueueRow if notification datastructure is set
        // we send the work queue data anyway as a parameter (to all workflows)
        DataRow workQueueRow = null;
        if (wqc.NotificationStructure != null)
        {
            workQueueRow = queueItem.Tables[0].Rows[0];
        }
        return workQueueRow;
    }
    /// <summary>
    /// Prepare a data to be sent as a notification.
    /// </summary>
    /// <param name="notificationTemplateId">Id of message template</param>
    /// <param name="notificationSource">notificationSource Input context for transformation - data taken either from WorkQueue Entry or fetched by refId </param>
    /// <param name="recipientRow">Recipient info</param>
    /// <param name="transactionId">current db transaction</param>
    /// <returns>Xml containing in the first row of the first table non-empty fields named "Body" and "Subject"</returns>
    public IDataDocument GenerateNotificationMessage(
            Guid notificationTemplateId
            , IXmlContainer notificationSource
            , DataRow recipientRow
            , DataRow workQueueRow
            , string transactionId
        )
    {
        OrigamNotificationContactData.OrigamNotificationContactRow recipient =
            recipientRow as OrigamNotificationContactData.OrigamNotificationContactRow;
        if (recipient == null)
        {
            throw new Exception("Recipient must be type OrigamNotificationContactData.OrigamNotificationContactRow.");
        }
        IPersistenceService persistence = ServiceManager.Services.GetService(typeof(IPersistenceService)) as IPersistenceService;
        
        using (LanguageSwitcher langSwitcher = new LanguageSwitcher(
            recipient != null && !recipient.IsLanguageTagIETFNull() ?
                recipient.LanguageTagIETF : ""))
        {
            // get the current localized XSLT template
            DataSet templateData = dataService.LoadData(new Guid("92c3c8b4-68a3-482b-8a90-f7142c4b17ec"), // OrigamNotificationTemplate DS
                                                             new Guid("3724bd2a-9466-4129-bdfa-ca8dc8621a72"), // GetId
                                                             Guid.Empty, Guid.Empty, transactionId,
                                                             "OrigamNotificationTemplate_parId",
                                                             notificationTemplateId);
            string template = (string)templateData.Tables[0].Rows[0]["Template"];
            // transform
            DataStructure resultStructure = persistence.SchemaProvider.RetrieveInstance(typeof(DataStructure), new ModelElementKey(new Guid("2f5e1853-e885-4177-ab6d-9da52123ae82"))) as DataStructure;
            IXsltEngine transform = AsTransform.GetXsltEngine(
                XsltEngineType.XslCompiledTransform, persistence.SchemaProvider);
            Hashtable parameters = new Hashtable();
            if (recipient != null)
            {
                parameters["RecipientRow"] = DatasetTools.GetRowXml(recipient, DataRowVersion.Default);
            }
            if (workQueueRow != null)
            {
                parameters["WorkQueueRow"] = DatasetTools.GetRowXml(workQueueRow, DataRowVersion.Default);
            }
            IDataDocument notificationData = (IDataDocument)transform.Transform(notificationSource, template, parameters, transactionId, resultStructure, false);
            // return result
            return notificationData;
        }
    }
    private OrigamNotificationContactData GetNotificationContacts(Guid workQueueNotificationContactTypeId,
        Guid origamNotificationChannelTypeId, string value, IXmlContainer context,
        DataRow workQueueRow, string transactionId)
    {
        OrigamNotificationContactData result = new OrigamNotificationContactData();
        if (workQueueNotificationContactTypeId.Equals(new Guid("3535c6f5-c48d-4ae9-ba21-43852d4f66f8")))
        {
            // manual entry                
            OrigamNotificationContactData.OrigamNotificationContactRow recipient = result.OrigamNotificationContact.NewOrigamNotificationContactRow();
            recipient.ContactIdentification = value;
            result.OrigamNotificationContact.AddOrigamNotificationContactRow(recipient);
        }
        else
        {
            // anything else - we execute the workflow in order to get the addresses
            QueryParameterCollection pms = new QueryParameterCollection();
            pms.Add(new QueryParameter("workQueueNotificationContactTypeId", workQueueNotificationContactTypeId));
            pms.Add(new QueryParameter("OrigamNotificationChannelTypeId", origamNotificationChannelTypeId));
            pms.Add(new QueryParameter("value", value));
            pms.Add(new QueryParameter("context", context));
            if (workQueueRow != null)
            {
                pms.Add(new QueryParameter("WorkQueueRow",  DatasetTools.GetRowXml(workQueueRow, DataRowVersion.Default)));
            }
            IDataDocument wfResult = core.WorkflowService.ExecuteWorkflow(new Guid("1e621daf-c70d-4cc1-9a52-73427c499006"), pms, transactionId) as IDataDocument;
            if (wfResult != null)
            {
                DatasetTools.MergeDataSetVerbose(result, wfResult.DataSet);
            }
        }
        return result;
    }
    private void WorkQueueRowFill(WorkQueueClass wqc, RuleEngine ruleEngine, DataRow row, IXmlContainer data)
    {
        foreach (WorkQueueClassEntityMapping em in wqc.EntityMappings)
        {
            if (em.XPath != "" && em.XPath != null)
            {
                DataColumn col = row.Table.Columns[em.Name];
                OrigamDataType dataType = (OrigamDataType)col.ExtendedProperties["OrigamDataType"];
                object value = ruleEngine.EvaluateContext(em.XPath, data, dataType, wqc.WorkQueueStructure);
                string sValue = (value as string);
                if (sValue != null && col.MaxLength > 0 & sValue.Length > col.MaxLength)
                {
                    // handle string length
                    row[em.Name] = sValue.Substring(0, col.MaxLength - 4) + " ...";
                }
                else
                {
                    row[em.Name] = (value == null ? DBNull.Value : value);
                }
            }
        }
        // set refId to self if it was not mapped to a source row id, so e.g. notifications can
        // load the work queue entry data
        if (row.IsNull("refId"))
        {
            row["refId"] = row["Id"];
        }
    }
    public DataSet FetchSingleQueueEntry(WorkQueueClass wqc, object queueEntryId, string transactionId)
    {
        DataStructureMethod getOneEntryMethod =
            wqc.WorkQueueStructure.GetChildByName("GetById",
            DataStructureMethod.CategoryConst) as DataStructureMethod;
        if (getOneEntryMethod == null)
        {
            throw new OrigamException(String.Format("Programming Error: Can't find a filterset called `GetById' in DataStructure `{0}'. Please add the filterset to the DataStructure.",
                wqc.WorkQueueStructure.Name));
        }
        // fetch entry by Id
        DataSet queueEntryDS = dataService.LoadData(wqc.WorkQueueStructureId,
            getOneEntryMethod.Id, Guid.Empty, Guid.Empty, transactionId,
            "WorkQueueEntry_parId", queueEntryId);
        if (queueEntryDS.Tables[0].Rows.Count != 1)
        {
            RuleException exc = new RuleException(
                ResourceUtils.GetString("ErrorWorkQueueEntryNotFound"),
                RuleExceptionSeverity.High,
                "ErrorWorkQueueEntryNotFound", "WorkQueueEntry");
            throw exc;
        }
        return queueEntryDS;
    }
    public void WorkQueueRemove(Guid workQueueId, object queueEntryId, string transactionId)
    {
        if (queueEntryId == null) return;
        WorkQueueData queue = GetQueue(workQueueId);
        WorkQueueData.WorkQueueRow queueRow = queue.WorkQueue[0];
        if (log.IsDebugEnabled)
        {
            log.Debug("Removing Work Queue Entries for Queue: " + queueRow.Name);
        }
        WorkQueueClass wqc = workQueueUtils.WorkQueueClass(queueRow.WorkQueueClass);
        if (wqc != null)
        {
            DataSet queueEntryDS = FetchSingleQueueEntry(wqc, queueEntryId, transactionId);
            queueEntryDS.Tables[0].Rows[0].Delete();
            try
            {
                dataService.StoreData(wqc.WorkQueueStructure.Id, queueEntryDS, false, transactionId);
            }
            catch (DBConcurrencyException)
            {
                dataService.StoreData(
                    new Guid("7ca0c208-9ac8-4c55-bd0e-32575b613654"),
                    queueEntryDS, false, transactionId);
            }
            if (log.IsDebugEnabled)
            {
                log.Debug(string.Format("Removed Work Queue Entry `{0}'  from Queue: {1}",
                    queueEntryId, queueRow.Name));
            }
        }
    }
    public void WorkQueueRemove(string workQueueClass, string workQueueName, Guid workQueueId, string condition, object rowKey, string transactionId)
    {
        if (rowKey == null) return;
        if (log.IsDebugEnabled)
        {
            log.Debug("Removing Work Queue Entries for Queue: " + workQueueName + " for row Id " + rowKey);
        }
        WorkQueueClass wqc = workQueueUtils.WorkQueueClass(workQueueClass);
        if (wqc != null)
        {
            // get queue entries for this row and queue
            DataStructureMethod pkMethod = wqc.WorkQueueStructure.GetChildByName("GetByMasterId", DataStructureMethod.CategoryConst) as DataStructureMethod;
            if (pkMethod == null)
            {
                throw new Exception("GetByMasterId method not found for data structure of work queue class '" + wqc.Name + "'");
            }
            DataSet ds = dataService.LoadData(wqc.WorkQueueStructureId, pkMethod.Id, Guid.Empty, Guid.Empty, transactionId, "WorkQueueEntry_parRefId", rowKey, "WorkQueueEntry_parWorkQueueId", workQueueId);
            int count = ds.Tables[0].Rows.Count;
            if (count > 0)
            {
                foreach (DataRow rowToDelete in ds.Tables[0].Rows)
                {
                    bool delete = true;
                    if (condition != null && condition != string.Empty)
                    {
                        if (!EvaluateWorkQueueCondition(rowToDelete, condition, workQueueName, transactionId))
                        {
                            delete = false;
                            count--;
                        }
                    }
                    if (delete) rowToDelete.Delete();
                }
                dataService.StoreData(wqc.WorkQueueStructure.Id, ds, false, transactionId);
            }
            if (log.IsDebugEnabled)
            {
                log.Debug("Removed " + count.ToString() + " work Queue Entries from Queue: " + workQueueName);
            }
        }
    }
    public void WorkQueueUpdate(string workQueueClass, int relationNo, Guid workQueueId, object rowKey, string transactionId)
    {
        if (rowKey == null) return;
        WorkQueueClass wqc = workQueueUtils.WorkQueueClass(workQueueClass);
        RuleEngine ruleEngine = RuleEngine.Create(new Hashtable(), transactionId);
        UserProfile profile = SecurityManager.CurrentUserProfile();
        // get filterset for this relation no (1-7)
        string filterSetName = null;
        DataStructureFilterSet fs = null;
        if (relationNo == 0)
        {
            filterSetName = "GetByMasterId";
        }
        else if (relationNo > 0 & relationNo < 8)
        {
            filterSetName = "GetByRel" + relationNo.ToString();
        }
        else
        {
            throw new ArgumentOutOfRangeException("relationNo", relationNo, ResourceUtils.GetString("ErrorMaxWorkQueueEntities"));
        }
        fs = (DataStructureFilterSet)wqc.WorkQueueStructure.GetChildByName(filterSetName, DataStructureFilterSet.CategoryConst);
        if (fs == null)
        {
            throw new Exception(ResourceUtils.GetString("ErrorNoFilterSet", wqc.WorkQueueStructure.Path, filterSetName));
        }
        // load all entries in the queue related to this entity
        DataSet entries = dataService.LoadData(wqc.WorkQueueStructureId, fs.Id, Guid.Empty, Guid.Empty, transactionId, "WorkQueueEntry_parRefId", rowKey, "WorkQueueEntry_parWorkQueueId", workQueueId);
        string pkParamName = null;
        foreach (string parameterName in wqc.EntityStructurePrimaryKeyMethod.ParameterReferences.Keys)
        {
            pkParamName = parameterName;
        }
        if (pkParamName == null)
        {
            throw new OrigamException(string.Format("Entity Structure Primary Key Method '{0}' specified in a work queue class '{1}' has no parameters. A parameter to load a record by its primary key is expected.",
                wqc.EntityStructurePrimaryKeyMethod.Path, wqc.Path));
        }
        // for-each entry
        foreach (DataRow entry in entries.Tables[0].Rows)
        {
            // load original record
            DataSet originalRecord = dataService.LoadData(wqc.EntityStructureId, wqc.EntityStructurePkMethodId, Guid.Empty, Guid.Empty, transactionId, pkParamName, entry["refId"]);
            // record could have been deleted in the meantime, we test
            if (originalRecord.Tables[0].Rows.Count > 0)
            {
                IXmlContainer data = DatasetTools.GetRowXml(originalRecord.Tables[0].Rows[0], DataRowVersion.Default);
                // update entry from record
                WorkQueueRowFill(wqc, ruleEngine, entry, data);
                entry["RecordUpdated"] = DateTime.Now;
                entry["RecordUpdatedBy"] = profile.Id;
            }
        }
        // save entries
        StoreQueueItems(wqc, entries.Tables[0], transactionId);
    }
    #endregion
    /// <summary>
    /// Handles work queue actions from the UI. WILL NOT CREATE TRANSACTIONS!
    /// </summary>
    /// <param name="queueClass"></param>
    /// <param name="selectedRows"></param>
    /// <param name="commandType"></param>
    /// <param name="command"></param>
    /// <param name="param1"></param>
    /// <param name="param2"></param>
    /// <param name="errorQueueId"></param>
    public void HandleAction(Guid queueId, string queueClass, DataTable selectedRows, Guid commandType,
        string command, string param1, string param2, object errorQueueId)
    {
        try
        {
            WorkQueueData workQueueData = GetQueue(queueId);
            WorkQueueData.WorkQueueRow queue = workQueueData.WorkQueue[0];
            HandleAction(queue, queueClass, selectedRows, commandType, command, param1, param2, true, errorQueueId, null);
        }
        catch
        {
            // quit silently if work queue is defined because it was moved to error queue already
            if (errorQueueId == null)
            {
                throw;
            }
        }
    }
    private static void CheckSelectedRowsCountPositive(int count)
    {
        if (count == 0)
        {
            throw new RuleException(ResourceUtils.GetString("ErrorNoRecordsSelectedForAction"));
        }
    }
    private void HandleAction(WorkQueueData.WorkQueueRow queue, string queueClass, DataTable selectedRows, Guid commandType,
        string command, string param1, string param2, bool lockItems, object errorQueueId, string transactionId)
    {
        if (log.IsInfoEnabled)
        {
            log.Info("Begin HandleAction() queue class: " + queueClass + " command: " + command + " lockItems: " + lockItems.ToString());
        }
        // set all rows to be actual values, not added (in case the calling function did not do that)
        selectedRows.AcceptChanges();
        WorkQueueClass wqc = workQueueUtils.WorkQueueClass(queueClass);
        try
        {
            if (lockItems) LockQueueItems(wqc, selectedRows);
            IParameterService ps = ServiceManager.Services.GetService(typeof(IParameterService)) as IParameterService;
            if (commandType == (Guid)ps.GetParameterValue("WorkQueueCommandType_StateChange"))
            {
                HandleStateChange(queueClass, selectedRows, param1, param2, transactionId);
            }
            else if (commandType == (Guid)ps.GetParameterValue("WorkQueueCommandType_Remove"))
            {
                HandleRemove(queueClass, selectedRows, transactionId);
            }
            else if (commandType == (Guid)ps.GetParameterValue("WorkQueueCommandType_Move"))
            {
                HandleMove(queueClass, selectedRows, param1, transactionId, true);
            }
            else if (commandType == (Guid)ps.GetParameterValue("WorkQueueCommandType_WorkQueueClassCommand"))
            {
                HandleWorkflow(queueClass, selectedRows, command, param1, param2, transactionId);
            }
            else if (commandType == (Guid)ps.GetParameterValue("WorkQueueCommandType_Archive"))
            {
                HandleMove(queueClass, selectedRows, param1, transactionId, false);
            }
            else if (commandType == (Guid)ps.GetParameterValue("WorkQueueCommandType_LoadFromExternalSource"))
            {
                LoadFromExternalSource(queue.Id);
            }
            else if (commandType == (Guid)ps.GetParameterValue("WorkQueueCommandType_RunNotifications"))
            {
                HandleRunNotifications(queueClass, selectedRows, transactionId);
            }
            else
            {
                throw new ArgumentOutOfRangeException("commandType", commandType, ResourceUtils.GetString("ErrorUnknownWorkQueueCommand"));
            }
            if (lockItems) UnlockQueueItems(wqc, selectedRows);
        }
        catch (WorkQueueItemLockedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (log.IsErrorEnabled)
            {
                log.LogOrigamError("Error occured while processing work queue items., Queue: " + wqc?.Name + ", Command: " + command, ex);
            }
            if (transactionId != null)
            {
                ResourceMonitor.Rollback(transactionId);
            }
            // rollback deletion of the row when it fails - we have to work with previous state
            bool anyRowInRetry = false;
            foreach (DataRow row in selectedRows.Rows)
            {
                if (row.RowState == DataRowState.Deleted)
                {
                    row.RejectChanges();
                }
                retryManager.SetEntryRetryData(row, queue, ex.Message);
                anyRowInRetry = anyRowInRetry || (bool)row["InRetry"];
            }
            StoreFailedEntries(wqc, selectedRows);
            // other failure => move to the error queue, if available
            if (!anyRowInRetry && errorQueueId != null)
            {
                HandleMoveQueue(wqc, selectedRows, (Guid)errorQueueId, ex.Message, null, false);
            }
            // unlock the queue item
            if (lockItems) UnlockQueueItems(wqc, selectedRows, true);
            throw;
        }
        if (log.IsInfoEnabled)
        {
            log.Info("Finished HandleAction() queue class: " + queueClass + " command: " + command);
        }
    }
    public void HandleAction(string workQueueCode, string commandText, Guid queueEntryId)
    {
        Guid queueId = workQueueUtils.GetQueueId(workQueueCode);
        // get all queue data from database (no entries)
        WorkQueueData queue = GetQueue(queueId);
        // extract WorkQueueClass name and construct WorkQueueClass from name
        WorkQueueData.WorkQueueRow queueRow = queue.WorkQueue[0];
        WorkQueueClass wqc = (WorkQueueClass)WQClass(queueRow.WorkQueueClass);
        // authorize access from API
        IOrigamAuthorizationProvider auth = SecurityManager.GetAuthorizationProvider();
        if (queueRow.IsApiAccessRolesNull() || !auth.Authorize(SecurityManager.CurrentPrincipal, queueRow.ApiAccessRoles))
        {
            throw new RuleException(
                String.Format(ResourceUtils.GetString("ErrorWorkQueueApiNotAuthorized"),
                    queueId),
                RuleExceptionSeverity.High, "queueId", "");
        }
        // find command in the queue data 
        WorkQueueData.WorkQueueCommandRow commandRow = null;
        foreach (WorkQueueData.WorkQueueCommandRow cmd in queue.WorkQueueCommand.Rows)
        {
            if (cmd.Text == commandText)
            {
                commandRow = cmd;
            }
        }
        if (commandRow.IsRolesNull() || !auth.Authorize(SecurityManager.CurrentPrincipal, commandRow.Roles))
        {
            throw new RuleException(
                String.Format(ResourceUtils.GetString("ErrorWorkQueueCommandNotAuthorized"),
                    commandText, queueId),
                RuleExceptionSeverity.High, "commandId", "");
        }
        // fetch a single queue entry
        DataSet queueEntryDS = FetchSingleQueueEntry(wqc, queueEntryId, null);
        // call handle action
        HandleAction(queueRow, queueRow.WorkQueueClass, queueEntryDS.Tables[0],
            commandRow.refWorkQueueCommandTypeId,
            commandRow.IsCommandNull() ? null : commandRow.Command,
            commandRow.IsParam1Null() ? null : commandRow.Param1,
            commandRow.IsParam2Null() ? null : commandRow.Param2,
            true,
            commandRow.IsrefErrorWorkQueueIdNull() ? (object)null : (object)commandRow.refErrorWorkQueueId,
            null);
    }
    
    public DataRow GetNextItem(string workQueueName, string transactionId,
        bool processErrors)
    {
        var queueId = workQueueUtils.GetQueueId(workQueueName);
        WorkQueueData.WorkQueueRow queue = GetQueue(queueId).WorkQueue[0];
        return queueProcessor.GetNextItem(queue, transactionId,
            processErrors, cancellationTokenSource.Token);
    }
    private void LockQueueItems(WorkQueueClass wqc, DataTable selectedRows)
    {
        if (!workQueueUtils.LockQueueItems(wqc, selectedRows))
        {
            throw new WorkQueueItemLockedException();
        }
    }
    /// <summary>
    /// Unlocks work queue entries in a separate (new) database transaction
    /// </summary>
    /// <param name="wqc">Work queue class schema item</param>
    /// <param name="selectedRows">Work queue entries to unlock </param>
    /// <param name="rejectChangesWhenDeleted">This should be set if you call the
    /// function from within catch handler. In that case the original transaction
    /// has been rollbacked and even already removed entries has been recreated.
    /// We need to reflect that state in the dataset (reject changes)
    /// and that way unlock deleted rows.
    /// </param>
    private void UnlockQueueItems(WorkQueueClass wqc, DataTable selectedRows, bool rejectChangesWhenDeleted = false)
    {
        foreach (DataRow row in selectedRows.Rows)
        {
            if (rejectChangesWhenDeleted && row.RowState == DataRowState.Deleted)
            {
                row.RejectChanges();
            }
            if (row.RowState != DataRowState.Deleted)
            {
                Guid id = (Guid)row["Id"];
                if (log.IsDebugEnabled)
                {
                    log.Debug("Unlocking work queue item id " + id.ToString());
                }
                // load the fresh queue entry from the database, because
                // it may have been e.g. deleted after state change (in that case
                // nothing will happen/nothing will be unlocked)
                DataSet data = dataService.LoadData(
                    new Guid("59de7db2-e2f4-437b-b191-0fd3bc766685"),
                    new Guid("a68a8990-f476-4a64-bb9a-e45228eb9aae"),
                    Guid.Empty, Guid.Empty, null,
                    "WorkQueueEntry_parId", id
                    );
                foreach (DataRow freshRow in data.Tables[0].Rows)
                {
                    freshRow["IsLocked"] = false;
                    freshRow["refLockedByBusinessPartnerId"] = DBNull.Value;
                }
                dataService.StoreData(
                    new Guid("59de7db2-e2f4-437b-b191-0fd3bc766685"),
                    data, false, null);
            }
        }
    }
    private void HandleMoveQueue(WorkQueueClass wqc, DataTable selectedRows, 
        Guid newQueueId, string errorMessage, string transactionId, 
        bool resetErrors)
    {
        foreach (DataRow row in selectedRows.Rows)
        {
            if (log.IsInfoEnabled)
            {
                log.RunHandled(() =>
                {
                    Guid itemId = (Guid)row["Id"];
                    log.Info("Moving queue item " + itemId + 
                        " to queue id " + newQueueId +
                        (errorMessage == null ? "" : " with error: " 
                        + errorMessage));
                });
            }
            row["refWorkQueueId"] = newQueueId;
            retryManager.ResetEntry(row);
            if (resetErrors || errorMessage == null)
            {
                row["ErrorText"] = DBNull.Value;
            }
            else
            {
                row["ErrorText"] = errorMessage;
            }
        }
        try
        {
            StoreQueueItems(wqc, selectedRows, transactionId);
        }
        catch (DBConcurrencyException)
        {
            dataService.StoreData(
                new Guid("7ca0c208-9ac8-4c55-bd0e-32575b613654"),
                selectedRows.DataSet, false, transactionId);
        }
        foreach (DataRow row in selectedRows.Rows)
        {
            DataSet slice = DatasetTools.CloneDataSet(selectedRows.DataSet);
            DatasetTools.GetDataSlice(slice, new List<DataRow> { row });
            if (log.IsInfoEnabled)
            {
                log.RunHandled(() =>
                {
                    Guid itemId = (Guid)row["Id"];
                    log.Info("Running notifications for item " + itemId + ".");
                });
            }
            ProcessNotifications(wqc, newQueueId, new Guid(WQ_EVENT_ONCREATE),
                slice, transactionId);
        }
    }
    private void HandleStateChange(string queueClass, DataTable selectedRows, string fieldName, string newValue, string transactionId)
    {
        CheckSelectedRowsCountPositive(selectedRows.Rows.Count);
        WorkQueueClass wqc = (WorkQueueClass)WQClass(queueClass);
        foreach (DataRow row in selectedRows.Rows)
        {
            // get the original record by refId
            string pkParamName = null;
            foreach (string parameterName in wqc.EntityStructurePrimaryKeyMethod.ParameterReferences.Keys)
            {
                pkParamName = parameterName;
            }
            DataSet ds = dataService.LoadData(wqc.EntityStructureId, wqc.EntityStructurePkMethodId, Guid.Empty, Guid.Empty, transactionId, pkParamName, row["refId"]);
            if (ds.Tables[0].Rows.Count == 0)
            {
                throw new Exception(ResourceUtils.GetString("ErrorNoRecords"));
            }
            if (!ds.Tables[0].Columns.Contains(fieldName))
            {
                throw new ArgumentOutOfRangeException("fieldName", fieldName, ResourceUtils.GetString("ErrorSourceFieldNotFound"));
            }
            Type t = ds.Tables[0].Columns[fieldName].DataType;
            object value;
            if (t == typeof(string))
            {
                value = newValue;
            }
            else if (t == typeof(Guid))
            {
                value = new Guid(newValue);
            }
            else if (t == typeof(int))
            {
                value = XmlConvert.ToInt32(newValue);
            }
            else
            {
                throw new Exception(ResourceUtils.GetString("ErrorConvertToType", t.ToString()));
            }
            ds.Tables[0].Rows[0][fieldName] = value;
            dataService.StoreData(wqc.EntityStructureId, ds, false, transactionId);
        }
    }
    private void HandleRunNotifications(string queueClass, DataTable selectedRows, string transactionId)
    {
        CheckSelectedRowsCountPositive(selectedRows.Rows.Count);
        WorkQueueClass wqc = (WorkQueueClass)WQClass(queueClass);
        IParameterService ps = ServiceManager.Services.GetService(typeof(IParameterService)) as IParameterService;
        foreach (DataRow row in selectedRows.Rows)
        {
            DataSet slice = DatasetTools.CloneDataSet(selectedRows.DataSet);
            DatasetTools.GetDataSlice(slice, new List<DataRow> { row });
            if (log.IsInfoEnabled)
            {
                log.RunHandled(() =>
                {
                    Guid itemId = (Guid)row["Id"];
                    log.Info("Running notifications for item " +
                             itemId + ".");
                });
            }
            ProcessNotifications(wqc, (Guid)row["refWorkQueueId"],
                 (Guid)ps.GetParameterValue("WorkQueueNotificationEvent_Command"),
                 slice, transactionId);
        }
    }
    private void HandleWorkflow(string queueClass, DataTable selectedRows, string command, string param1, string param2, string transactionId)
    {
        CheckSelectedRowsCountPositive(selectedRows.Rows.Count);
        WorkQueueClass wqc = (WorkQueueClass)WQClass(queueClass);
        WorkQueueWorkflowCommand cmd = wqc.GetCommand(command);
        QueryParameterCollection parameters = new QueryParameterCollection();
        foreach (WorkQueueWorkflowCommandParameterMapping pm in cmd.ParameterMappings)
        {
            object val;
            switch (pm.Value)
            {
                case WorkQueueCommandParameterMappingType.QueueEntries:
                    val = GetDataDocumentFactory(selectedRows.DataSet);
                    break;
                case WorkQueueCommandParameterMappingType.Parameter1:
                    val = param1;
                    break;
                case WorkQueueCommandParameterMappingType.Parameter2:
                    val = param2;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Value", pm.Value, ResourceUtils.GetString("ErrorUnknownWorkQueueCommandValue"));
            }
            parameters.Add(new QueryParameter(pm.Name, val));
        }
        core.WorkflowService.ExecuteWorkflow(cmd.WorkflowId, parameters, transactionId);
    }
    private object GetDataDocumentFactory(DataSet dataSet)
    {
        var val = dataSet.ExtendedProperties["IDataDocument"] ?? DataDocumentFactory.New(dataSet);
        if (dataSet.ExtendedProperties["IDataDocument"] == null)
        {
            dataSet.ExtendedProperties.Add("IDataDocument", val);
        }
        return val;
    }
    private void HandleRemove(string queueClass, DataTable selectedRows, string transactionId)
    {
        CheckSelectedRowsCountPositive(selectedRows.Rows.Count);
        if (log.IsInfoEnabled)
        {
            log.Info("Begin HandleRemove() queue class: " + queueClass);
        }
        WorkQueueClass wqc = (WorkQueueClass)WQClass(queueClass);
        foreach (DataRow row in selectedRows.Rows)
        {
            row.Delete();
        }
        try
        {
            StoreQueueItems(wqc, selectedRows, transactionId);
        }
        catch (DBConcurrencyException)
        {
            dataService.StoreData(
                new Guid("fb5d8abe-99b8-4ca0-871a-c8c6e3ae6b76"),
                selectedRows.DataSet, false, transactionId);
        }
        if (log.IsInfoEnabled)
        {
            log.Info("Finished HandleRemove() queue class: " + queueClass);
        }
    }
    private void HandleMove(string queueClass, DataTable selectedRows, string newQueueReferenceCode,
        string transactionId, bool resetErrors)
    {
        CheckSelectedRowsCountPositive(selectedRows.Rows.Count);
        if (log.IsInfoEnabled)
        {
            log.Info("Begin HandleMove() queue class: " + queueClass);
        }
        Guid newQueueId = workQueueUtils.GetQueueId(newQueueReferenceCode);
        WorkQueueClass wqc = (WorkQueueClass)WQClass(queueClass);
        HandleMoveQueue(wqc, selectedRows, newQueueId, null, transactionId, resetErrors);
        if (log.IsInfoEnabled)
        {
            log.Info("Finished HandleMove() queue class: " + queueClass);
        }
    }
    private void StoreQueueItems(WorkQueueClass wqc, DataTable selectedRows, string transactionId)
    {
        dataService.StoreData(wqc.WorkQueueStructureId, selectedRows.DataSet, true, transactionId);
    }
    public WorkQueueData GetQueues(bool activeOnly=true, 
        string transactionId = null)
    {
        OrigamSettings settings 
            = ConfigurationManager.GetActiveConfiguration() ;
        WorkQueueData queues = new WorkQueueData();
        
        Guid filterMethodGuid = activeOnly
            ? new Guid("b1f1abcd-c8bc-4680-8f21-06a68e8305f0")
            : Guid.Empty;
        
        queues.Merge(dataService.LoadData(
            new Guid("7b44a488-ac98-4fe1-a427-55de0ff9e12e"),
            filterMethodGuid,
            Guid.Empty,
            new Guid("c1ec9d9e-09a2-47ad-b5e4-b57107c4dc34"),
            transactionId,
            "WorkQueue_parQueueProcessor",
            settings.Name));
        return queues;
    }
    private WorkQueueData GetQueue(Guid queueId)
    {
        WorkQueueData queues = new WorkQueueData();
        queues.Merge(dataService.LoadData(
            new Guid("7b44a488-ac98-4fe1-a427-55de0ff9e12e"),
            new Guid("2543bbd3-3592-4b14-9d74-86d0e9c65d98"),
            Guid.Empty,
            new Guid("c1ec9d9e-09a2-47ad-b5e4-b57107c4dc34"),
            null,
            "WorkQueue_parId", queueId));
        return queues;
    }
    bool _externalQueueAdapterBusy = false;
    private void LoadExternalWorkQueuesElapsed(object sender, ElapsedEventArgs e)
    { 
        LoadFromExternalSource();
    }
    private void LoadFromExternalSource()
    {
        LoadFromExternalSource(Guid.Empty);
    }
    private void LoadFromExternalSource(Guid queueId)
    {
        ISchemaService schemaService
            = ServiceManager.Services.GetService(typeof(SchemaService)) 
            as ISchemaService;
        if(_externalQueueAdapterBusy || !schemaService.IsSchemaLoaded
        || serviceBeingUnloaded)
        {
            if(log.IsDebugEnabled)
            {
                log.DebugFormat(
                    "Skipping external work queues load: adapterBusy: {0}, schemaLoaded: {1}, serviceBeingUnloaded: {2}",
                    _externalQueueAdapterBusy, schemaService?.IsSchemaLoaded, 
                    serviceBeingUnloaded);
            }
            return;
        }
        SecurityManager.SetServerIdentity();
        _externalQueueAdapterBusy = true;
        if(log.IsInfoEnabled)
        {
            log.Info("Starting loading external work queues.");
        }
        try
        {
            WorkQueueData queues = GetQueues();
            foreach(WorkQueueData.WorkQueueRow q in queues.WorkQueue.Rows)
            {
                if(queueId == Guid.Empty || q.Id == queueId)
                {
                    if(!q.IsrefWorkQueueExternalSourceTypeIdNull())
                    {
                        if(log.IsInfoEnabled)
                        {
                            log.Info("Starting loading external work queue " + q.Name);
                        }
                        try
                        {
                            ProcessExternalQueue(q);
                            StoreQueues(queues);
                        }
                        catch (Exception ex)
                        {
                            if(log.IsErrorEnabled)
                            {
                                log.LogOrigamError("Failed loading external work queue " + q.Name, ex);
                            }
                            q.ExternalSourceLastMessage = ex.Message;
                            q.ExternalSourceLastTime = DateTime.Now;
                            StoreQueues(queues);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (log.IsErrorEnabled) log.LogOrigamError("External queue load failed.", ex);
        }
        finally
        {
            _externalQueueAdapterBusy = false;
        }
        if (log.IsInfoEnabled) log.Info("Finished loading external work queues.");
    }
    private void StoreQueues(WorkQueueData queues)
    {
        dataService.StoreData(new Guid("7b44a488-ac98-4fe1-a427-55de0ff9e12e"), queues, false, null);
    }
    private bool HasAutoCommand(WorkQueueData.WorkQueueRow queue)
    {
        foreach (WorkQueueData.WorkQueueCommandRow cmd in queue.GetWorkQueueCommandRows())
        {
            if (cmd.IsAutoProcessed)
            {
                return true;
            }
        }
        return false;
    }
    private void ProcessQueueItem(WorkQueueData.WorkQueueRow queue, DataRow queueEntryRow)
    {
        WorkQueueClass wqc = workQueueUtils.WorkQueueClass(queue.WorkQueueClass);
        IParameterService ps = ServiceManager.Services.GetService(typeof(IParameterService)) as IParameterService;
        log.Info(
            $"Running ProcessQueueItem in Thread: {Thread.CurrentThread.ManagedThreadId}");
        
        string itemId = queueEntryRow["Id"].ToString();
        string transactionId = Guid.NewGuid().ToString();
        try
        {
            foreach (WorkQueueData.WorkQueueCommandRow cmd in queue
                         .GetWorkQueueCommandRows())
            {
                try
                {
                    if (IsAutoProcessed(cmd, queue, queueEntryRow,
                            transactionId))
                    {
                        if (log.IsInfoEnabled)
                        {
                            log.Info(
                                "Auto processing work queue item. Id: " +
                                itemId + ", Queue: "
                                + queue.Name + ", Command: " + cmd?.Text);
                        }
                        string param1 = null;
                        string param2 = null;
                        string command = null;
                        object errorQueueId = null;
                        if (!cmd.IsParam1Null()) param1 = cmd.Param1;
                        if (!cmd.IsParam2Null()) param2 = cmd.Param2;
                        if (!cmd.IsCommandNull()) command = cmd.Command;
                        if (!cmd.IsrefErrorWorkQueueIdNull())
                            errorQueueId = cmd.refErrorWorkQueueId;
                        // actual processing
                        HandleAction(queue, queue.WorkQueueClass,
                            queueEntryRow.Table,
                            cmd.refWorkQueueCommandTypeId,
                            command, param1, param2, false, errorQueueId,
                            transactionId);
                        if (log.IsInfoEnabled)
                        {
                            log.Info(
                                "Finished auto processing work queue item. Id: " +
                                itemId + ", Queue: "
                                + queue.Name + ", Command: " + cmd.Text);
                        }
                        if (cmd.refWorkQueueCommandTypeId ==
                            (Guid)ps.GetParameterValue(
                                "WorkQueueCommandType_Remove"))
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // rollback the transaction
                    if (transactionId != null)
                    {
                        ResourceMonitor.Rollback(transactionId);
                    }
                    if (queueEntryRow.RowState == DataRowState.Deleted)
                    {
                        queueEntryRow.RejectChanges();
                    }
                    // unlock the queue item
                    UnlockQueueItems(wqc, queueEntryRow.Table, true);
                    // do not process any other commands on this queue entry
                    throw;
                }
            }
            // commit the transaction
            ResourceMonitor.Commit(transactionId);
            // unlock the queue item
            UnlockQueueItems(wqc, queueEntryRow.Table);
        }
        catch (Exception ex)
        {
            // Catch the command exception. Transaction is already rolled back, so we continue to another queue item.
            if (log.IsFatalEnabled)
            {
                log.Fatal(
                    "Queue item processing failed. Id: " + itemId +
                    ", Queue: " +
                    queue?.Name, ex);
            }
        }
        finally
        {
            workQueueThrottle.ReportProcessed(queue);
        }
    }
    
    private bool IsAutoProcessed(WorkQueueData.WorkQueueCommandRow cmd,
        WorkQueueData.WorkQueueRow q, DataRow queueRow, string transactionId)
    {
        if (!cmd.IsAutoProcessed)
        {
            return false;
        }
        if (!(bool)queueRow["InRetry"] &&
            !cmd.IsAutoProcessedWithErrors && 
            !queueRow.IsNull("ErrorText") && 
            (string)queueRow["ErrorText"] != "")
        {
            return false;
        }
        bool result = false;
        if(cmd.IsAutoProcessingConditionXPathNull() || cmd.AutoProcessingConditionXPath == String.Empty)
        {
            // no condition, we always process
            return true;
        }
        else if(! cmd.IsAutoProcessingConditionXPathNull() && cmd.AutoProcessingConditionXPath != String.Empty)
        {
            result = EvaluateWorkQueueCondition(queueRow, cmd.AutoProcessingConditionXPath, q.Name, transactionId);
        }
        return result;
    }
    private bool EvaluateWorkQueueCondition(DataRow queueRow, string condition, string queueName, string transactionId)
    {
        // condition, we evaluate the condition
        if(log.IsDebugEnabled)
        {
            log.Debug("Checking condition for work queue item. Id: " + queueRow["Id"].ToString() + ", Queue: " + queueName + ", Condition: " + condition);
        }
        try
        {
            // we have to do it from the copied dataset, because later the XmlDataDocument would be re-created, which
            // is not supported by the .net
            IDataDocument oneRowXml = DataDocumentFactory.New(queueRow.Table.DataSet.Copy());
            XPathNavigator nav = oneRowXml.Xml.CreateNavigator();
            nav.MoveToFirstChild();	// /ROOT/
            nav.MoveToFirstChild();	// WorkQueueEntry/
            var evaluationResult = (string)XpathEvaluator.Instance.Evaluate(
                xpath: condition,
                isPathRelative: false,
                returnDataType: OrigamDataType.String,
                nav: nav,
                contextPosition: null,
                transactionId: transactionId);
            if (log.IsDebugEnabled)
            {
                log.Debug("Condition for work queue item. Id: " + queueRow["Id"].ToString() + ", Queue: " + queueName + ", Condition: " + condition + " evaluated to " + evaluationResult);
            }
            try
            {
                if(XmlConvert.ToBoolean(evaluationResult))
                {
                    return true;
                }
            }
            catch
            {
                throw new Exception("Work queue condition did not return boolean value. Command will not be processed.");
            }
        }
        catch(Exception ex)
        {
            throw new Exception("Work queue condition evaluation failed. Command will not be processed.", ex);
        }
        return false;
    }
    
    private void StoreFailedEntries(WorkQueueClass wqc, DataTable queueEntryTable)
    {
        try
        {
            StoreQueueItems(wqc, queueEntryTable, null);
        } catch (DBConcurrencyException)
        {
            var dataStructureQuery = new DataStructureQuery {
                DataSourceId = new Guid("7a18149a-2faa-471b-a43e-9533d7321b44"),
                MethodId = new Guid("ea139b9a-3048-4cd5-bf9a-04a91590624a"),
                LoadActualValuesAfterUpdate = false };
            dataService.StoreData(dataStructureQuery,
                queueEntryTable.DataSet, null);
        }
    }
    
    private void ProcessExternalQueue(WorkQueueData.WorkQueueRow q)
    {
        string transactionId = Guid.NewGuid().ToString();
        WorkQueueLoaderAdapter adapter = null;
        if(log.IsInfoEnabled)
        {
            log.Info("Loading external work queue: " + q?.Name);
        }
        try
        {
            adapter = WorkQueueAdapterFactory.GetAdapter(
                q.refWorkQueueExternalSourceTypeId.ToString());
            if(adapter == null)
            {
                throw new Exception("External Source Adapter not found for queue " + q.Name);
            }
            adapter.Connect(this,
                q.Id,
                q.WorkQueueClass,
                q.IsExternalSourceConnectionNull() ? null : q.ExternalSourceConnection,
                q.IsExternalSourceUserNameNull() ? null : q.ExternalSourceUserName,
                q.IsExternalSourcePasswordNull() ? null : q.ExternalSourcePassword,
                transactionId);
            int itemCount = 0;
            string lastState = null;
            if(! q.IsExternalSourceStateNull())
            {
                lastState = q.ExternalSourceState;
            }
            // get first item
            WorkQueueAdapterResult queueItem = adapter.GetItem(lastState);
            while(queueItem != null)
            {
                itemCount++;
                lastState = queueItem.State;
                string creationCondition = (q.IsCreationConditionNull() 
                    ? null : q.CreationCondition);
                // put it to the queue
                using(new TransactionScope(TransactionScopeOption.Suppress))
                {
                    Guid qId = WorkQueueAdd(
                        q.WorkQueueClass, q.Name, q.Id, creationCondition, 
                        queueItem.Document, queueItem.Attachments, 
                        transactionId);
                }
                // next
                if(!serviceBeingUnloaded)
                {
                    queueItem = adapter.GetItem(lastState);
                }
                else
                {
                    if(log.IsDebugEnabled)
                    {
                        log.Debug(
                            "Service is being unloaded. Stopping retrieval.");
                    }
                    queueItem = null;
                }
            }
            ResourceMonitor.Commit(transactionId);
            adapter.Disconnect();
            q.ExternalSourceLastMessage = ResourceUtils.GetString(
                "OKMessage", itemCount.ToString());
            if(itemCount > 0)
            {
                if(lastState == null)
                {
                    q.SetExternalSourceStateNull();
                }
                else
                {
                    q.ExternalSourceState = lastState;
                }
            }
            q.ExternalSourceLastTime = DateTime.Now;
        }
        catch(Exception ex)
        {
            ResourceMonitor.Rollback(transactionId);
            if(adapter != null)
            {
                adapter.Disconnect();
            }
            q.ExternalSourceLastMessage = ResourceUtils.GetString("ErrorMessage", ex.Message);
            q.ExternalSourceLastTime = DateTime.Now;
            if(log.IsErrorEnabled) log.LogOrigamError("Failed to load queue " + q.Name, ex);
        }
    }
    private void LoadExternalWorkQueuesDisposed(object sender, EventArgs e)
    {
        _loadExternalWorkQueuesTimer.Elapsed -= new ElapsedEventHandler(LoadExternalWorkQueuesElapsed);
    }
    bool _queueAutoProcessBusy = false;
    public WorkQueueData.WorkQueueRow FindQueue(string queueRefCode) 
    {
        var schemaService = ServiceManager.Services
            .GetService(typeof(SchemaService)) as ISchemaService;
        if (!schemaService.IsSchemaLoaded)
        {
            throw new InvalidOperationException("schemaService is not loaded");
        }
        return GetQueues(activeOnly: false)
            .WorkQueue.Rows
            .Cast<WorkQueueData.WorkQueueRow>()
            .Where(row => row.ReferenceCode == queueRefCode)
            .Where(HasAutoCommand)
            .FirstOrDefault();
    }
    public TaskRunner GetAutoProcessorForQueue(string queueRefCode,
        int parallelism, int forceWait_ms)
    {
        var queueToProcess = FindQueue(queueRefCode);
        
        if (queueToProcess == null)
        {
            throw new ArgumentException($"Queue with code: {queueRefCode} " +
                                        $"does not exist, " +
                                        $"is empty " +
                                        $"or does not have autoprocess set.");
        }
        
        Action<CancellationToken> actionToRunInEachTask = cancellationToken =>
        {
            while (true)
            {
                try
                {
                    queueProcessor.ProcessAutoQueueCommands(queueToProcess, cancellationToken, forceWait_ms);
                }
                catch (OrigamException ex)
                {
                    if (IsDeadlock(ex))
                        HandleDeadLock();
                    else
                        throw;
                }
                
                const int millisToSleep = 1000;
                log.Info($"Worker in thread {Thread.CurrentThread.ManagedThreadId} " +
                         $"cannot get next Item from the queue, sleeping for " +
                         $"{millisToSleep} ms before trying again...");
                Thread.Sleep(millisToSleep);
            }
        };
        
        return new TaskRunner(actionToRunInEachTask, parallelism);
    }
    private static void HandleDeadLock()
    {
        Random r = new Random();
        int miliesToSleep = r.Next(1000, 2000);
        log.Warn(
            $"Deaclock was cought! Pausing thread" +
            $" {Thread.CurrentThread.ManagedThreadId} for {miliesToSleep} ms");
        Thread.Sleep(miliesToSleep);
    }
    private static bool IsDeadlock(OrigamException ex)
    {
        return ex.InnerException is SqlException &&
               ex.InnerException.Message.Contains("deadlocked");
    }
    private void WorkQueueAutoProcessTimerElapsed(object sender, ElapsedEventArgs e)
    {
        ISchemaService schemaService = ServiceManager.Services.GetService(typeof(SchemaService)) as ISchemaService;
        if(_queueAutoProcessBusy || !schemaService.IsSchemaLoaded
        || serviceBeingUnloaded)
        {
            if(log.IsDebugEnabled)
            {
                log.DebugFormat(
                    "Skipping auto processing work queues: queueAutoProcessBusy: {0}, schemaLoaded: {1}, serviceBeingUnloaded: {2}",
                    _queueAutoProcessBusy, schemaService.IsSchemaLoaded, 
                    serviceBeingUnloaded);
            }
            return;
        }
        SecurityManager.SetServerIdentity();
        _queueAutoProcessBusy = true;
        if(log.IsInfoEnabled) log.Info("Starting auto processing work queues.");
        try
        {
            IEnumerable<WorkQueueData.WorkQueueRow> queues = GetQueues()
                .WorkQueue.Rows
                .Cast<WorkQueueData.WorkQueueRow>()
                .Where(HasAutoCommand);
            queueProcessor.Run(queues, cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            if(log.IsErrorEnabled)
            {
                log.LogOrigamError("Unexpected error occured while autoprocessing workqueues.", ex);
            }
        }
        finally
        {
            _queueAutoProcessBusy = false;
            if(log.IsInfoEnabled)
            {
                log.Info("Finished auto processing work queues.");
            }
        }
    }
    private void WorkQueueAutoProcessTimerDisposed(object sender, EventArgs e)
    {
        _queueAutoProcessTimer.Elapsed -= WorkQueueAutoProcessTimerElapsed;
    }
    private void schemaService_SchemaLoaded(object sender,  bool isInteractive)
    {
        OrigamSettings settings = ConfigurationManager.GetActiveConfiguration() ;
        if(settings.LoadExternalWorkQueues)
        {
            if(log.IsInfoEnabled) log.Info("LoadExternalWorkQueues Enabled. Interval: " + settings.ExternalWorkQueueCheckPeriod);
            _loadExternalWorkQueuesTimer.Interval = settings.ExternalWorkQueueCheckPeriod * 1000;
            _loadExternalWorkQueuesTimer.Elapsed += LoadExternalWorkQueuesElapsed;
            _loadExternalWorkQueuesTimer.Disposed += LoadExternalWorkQueuesDisposed;
            _loadExternalWorkQueuesTimer.Start();
        }
        else
        {
            _loadExternalWorkQueuesTimer.Stop();
        }   
        
        if(settings.AutoProcessWorkQueues)
        {
            _queueAutoProcessTimer.Elapsed += WorkQueueAutoProcessTimerElapsed;
            _queueAutoProcessTimer.Disposed += WorkQueueAutoProcessTimerDisposed;
            _queueAutoProcessTimer.Start();
        }
        else
        {
            _queueAutoProcessTimer.Stop();
        }
    }
    void schemaService_SchemaUnloading(object sender, CancelEventArgs e)
    {
        // work queue shouldn't start new processing here
        // since schemaService.IsSchemaLoaded has been set to false
        // as the first think when the unload started.
        if(log.IsDebugEnabled)
        {
            log.Debug("schemaService_SchemaUnloading");
        }
        StopTasks();
    }
    void schemaService_SchemaUnloaded(object sender, EventArgs e)
    {
        if(log.IsDebugEnabled)
        {
            log.Debug("schemaService_SchemaUnloaded");
        }
    }
}

public static class WorkQueueRetryType
{
    public const string NoRetryString = "69460BCF-81D4-4A97-94F7-5A391D16F771";
    public const string LinearRetryString = "8A5C793F-73B8-41EF-A459-618A8E6FE4FA";
    public const string ExponentialRetryString = "57AD4C10-1F43-4CCF-A48A-132E7E418D53";
    
    public static readonly Guid NoRetry = new(NoRetryString);
    public static readonly Guid LinearRetry = new(LinearRetryString);
    public static readonly Guid ExponentialRetry = new(ExponentialRetryString);
}
