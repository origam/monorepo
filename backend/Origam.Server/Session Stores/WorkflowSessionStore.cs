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

#region license
/*
Copyright 2005 - 2021 Advantage Solutions, s. r. o.

This file is part of ORIGAM.

ORIGAM is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

ORIGAM is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with ORIGAM.  If not, see<http://www.gnu.org/licenses/>.
*/
#endregion

using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Xml;

using Origam.Workflow;
using Origam.Schema.EntityModel;
using Origam.Schema.WorkflowModel;
using Origam.Workbench.Services;
using Origam.Schema;
using System.Collections;
using Origam;
using Origam.Rule;
using Origam.Schema.MenuModel;
using Origam.DA;
using Origam.Gui;
using core = Origam.Workbench.Services.CoreServices;
using Origam.Schema.RuleModel;
using Origam.Server;
using Origam.Service.Core;

namespace Origam.Server
{
    public class WorkflowSessionStore : SaveableSessionStore
    {
        private bool _disposeAfterAction = false;
        private WorkflowHost _actualHost;
        private WorkflowHost _host;
        private Guid _taskId;
        private string _stepDescription;
        private IEndRule _endRule;
        private Guid _workflowId;
        private Guid _workflowInstanceId;
        private string _finishMessage;
        private AbstractDataStructure _dataStructure;
        private DataStructureMethod _refreshMethod;
        private Hashtable _parameters = new Hashtable();
        private bool _allowSave;
        private bool _isProcessing;
        private bool _isFinalForm;
        private bool _isAutoNext;
        private bool _isFirstSaveDone = false;

        public WorkflowSessionStore(IBasicUIService service, UIRequest request, Guid workflowId,
            string name, Analytics analytics)
            : base(service, request, name, analytics)
        {
            this.WorkflowId = workflowId;
        }
        
        #region Overriden SessionStore Methods

        public override bool HasChanges()
        {
            return AllowSave && Data != null && Data.HasChanges();
        }

        public override void Init()
        {
            IPersistenceService ps = ServiceManager.Services.GetService(typeof(IPersistenceService)) as IPersistenceService;
            IWorkflow workflow = ps.SchemaProvider.RetrieveInstance(typeof(AbstractSchemaItem), new ModelElementKey(this.WorkflowId)) as IWorkflow;

            WorkflowEngine engine = WorkflowEngine.PrepareWorkflow(workflow, new Hashtable(this.Request.Parameters), false, this.Request.Caption);
            _host = new WorkflowHost();
            _host.SupportsUI = true;
            WorkflowCallbackHandler handler = new WorkflowCallbackHandler(_host, engine.WorkflowInstanceId);
            handler.Subscribe();
            _host.ExecuteWorkflow(engine);
            handler.Event.WaitOne();

            HandleWorkflow(handler);
        }

        public override object ExecuteAction(string actionId)
        {
            return ExecuteAction(actionId, null);
        }

        public object ExecuteAction(string actionId, IList<string> cachedFormIds)
        {
            object result;

            if (this.IsProcessing)
            {
                throw new Exception(Resources.ErrorCommandInProgress);
            }

            this.IsProcessing = true;

            try
            {
                switch (actionId)
                {
                    case ACTION_QUERYNEXT:
                        result = EvaluateEndRule();
                        break;

                    case ACTION_NEXT:
                        result = HandleWorkflowNextAsync(cachedFormIds).Result;
                        break;

                    case ACTION_ABORT:
                        result = HandleAbortAsync().Result;
                        break;

                    case ACTION_SAVE:
                        result = this.Save();
                        this.IsFirstSaveDone = true;
                        break;

                    case ACTION_REFRESH:
                        result = this.HandleRefresh();
                        break;

                    default:
                        throw new ArgumentOutOfRangeException("actionId", actionId, Resources.ErrorContextUnknownAction);
                }
            }
            finally
            {
                this.IsProcessing = false;
            }

            if (_disposeAfterAction)
            {
                this.Dispose();
            }

            return result;
        }

        public override XmlDocument GetFormXml()
        {
            XmlDocument formXml;
            formXml = Origam.OrigamEngine.ModelXmlBuilders.FormXmlBuilder.GetXml(this.FormId, this.Title, true, new Guid(this.Request.ObjectId), this.FinishMessage, (this.DataStructure == null ? Guid.Empty : this.DataStructure.Id), false, "");
            XmlNodeList list = formXml.SelectNodes("/Window");
            XmlElement windowElement = list[0] as XmlElement;
            XmlElement uiRootElement = windowElement.SelectSingleNode("UIRoot") as XmlElement;

            if (this.RefreshMethod == null)
            {
                windowElement.SetAttribute("SuppressRefresh", "true");
            }

            if (this.IsAutoNext)
            {
                windowElement.SetAttribute("AutoWorkflowNext", "true");
            }

            if (!this.AllowSave)
            {
                windowElement.SetAttribute("SuppressSave", "true");
                // we MUST not turn on this.SupressSave here because that would
                // automatically accept all changes in workflow screens, resulting
                // in data not being saved by the workflow
                windowElement.SetAttribute("SuppressDirtyNotification", "true");
            }

            windowElement.SetAttribute("AskWorkflowClose", XmlConvert.ToString(this.AskWorkflowClose));

            if (this.IsFinalForm)
            {
                if (this.Request.Parameters.Count > 0)
                {
                    uiRootElement.SetAttribute("showWorkflowRepeatButton", "false");
                }
            }
            else
            {
                windowElement.SetAttribute("ShowWorkflowNextButton", "true");
                windowElement.SetAttribute("ShowWorkflowCancelButton", "true");
            }

            return formXml;
        }

        private DataSet LoadData()
        {
            DataSet data;
            QueryParameterCollection qparams = new QueryParameterCollection();
            foreach (DictionaryEntry entry in this.Parameters)
            {
                qparams.Add(new QueryParameter((string)entry.Key, entry.Value));
            }

            Guid methodId = Guid.Empty;
            if(_refreshMethod != null) methodId = _refreshMethod.Id;
            Guid sortSetId = Guid.Empty;
            if (this.SortSet != null) sortSetId = this.SortSet.Id;

            data = core.DataService.Instance.LoadData(_dataStructure.Id, methodId, Guid.Empty, sortSetId, null, qparams);
            return data;
        }

        public override void OnDispose()
        {
            if (this.TaskId != Guid.Empty)
            {
                ExecuteAction(SessionStore.ACTION_ABORT);
            }

            _host.Dispose();

            base.OnDispose();
        }

        public override string Title
        {
            get
            {
                if (this.IsModalDialog && this.IsFinalForm)
                {
                    return "";
                }
                else
                {
                    return base.Title;
                }
            }
            set
            {
                base.Title = value;
            }
        }
        #endregion

        #region Properties
        public WorkflowHost Host
        {
            get { return _actualHost; }
            set { _actualHost = value; }
        }

        public string StepDescription
        {
            get { return _stepDescription; }
            set 
            {
                _stepDescription = value;
                this.Title = value;
            }
        }

        public IEndRule EndRule
        {
            get { return _endRule; }
            set { _endRule = value; }
        }

        public new AbstractDataStructure DataStructure
        {
            get { return _dataStructure; }
            set { _dataStructure = value; }
        }

        public DataStructureMethod RefreshMethod
        {
            get { return _refreshMethod; }
            set { _refreshMethod = value; }
        }

        public Hashtable Parameters
        {
            get { return _parameters; }
            set { _parameters = value; }
        }

        public Guid TaskId
        {
            get { return _taskId; }
            set { _taskId = value; }
        }

        public Guid WorkflowId
        {
            get { return _workflowId; }
            set { _workflowId = value; }
        }

        public Guid WorkflowInstanceId
        {
            get { return _workflowInstanceId; }
            set { _workflowInstanceId = value; }
        }

        public string FinishMessage
        {
            get { return _finishMessage; }
            set { _finishMessage = value; }
        }

        public bool IsProcessing
        {
            get { return _isProcessing; }
            set { _isProcessing = value; }
        }

        public bool IsFirstSaveDone
        {
            get { return _isFirstSaveDone; }
            set { _isFirstSaveDone = value; }
        }

        public bool IsFinalForm
        {
            get { return _isFinalForm; }
            set { _isFinalForm = value; }
        }

        public bool IsAutoNext
        {
            get { return _isAutoNext; }
            set { _isAutoNext = value; }
        }

        public bool AllowSave
        {
            get { return _allowSave; }
            set { _allowSave = value; }
        }

        public bool AskWorkflowClose
        {
            get
            {
                return ! IsFinalForm;
            }
        }
        #endregion

        #region Private Methods
        private void HandleWorkflow(WorkflowCallbackHandler handler)
        {
            WorkflowHostFormEventArgs formArgs = handler.Result as WorkflowHostFormEventArgs;
            WorkflowHostMessageEventArgs messageArgs = handler.Result as WorkflowHostMessageEventArgs;

            if (handler.Result.Exception != null)
            {
                this.SetDataSource(null);
                this.FormId = new Guid(Origam.OrigamEngine.ModelXmlBuilders.FormXmlBuilder.WORKFLOW_FINISHED_FORMID);
                this.RuleSet = null;
                this.StepDescription = Resources.WorkflowErrorTitle;
                this.EndRule = null;
                this.DataStructure = null;
                this.DataStructureId = Guid.Empty;
                this.SortSet = null;
                this.RefreshMethod = null;
                this.Parameters = new Hashtable();
                this.Host = handler.Result.Engine.Host;
                this.TaskId = Guid.Empty;
                this.WorkflowInstanceId = handler.Result.Engine.WorkflowInstanceId;
                this.IsFinalForm = true;
                this.AllowSave = false;
                this.IsAutoNext = false;
                this.ConfirmationRule = null;
                this.Notifications.Clear();
                this.RefreshPortalAfterSave = false;

                // get the messages in reverse order
                ArrayList messages = new ArrayList();

                Exception ex = handler.Result.Exception;
                if (ex != null)
                {
                    StringBuilder message = new StringBuilder();
                    message.Append("<HTML><BODY>");
                    message.Append("<FONT COLOR=\"#FF0000\" SIZE=\"16\"><P>");
                    message.Append(ex.Message);
                    message.Append("</P></FONT>");
                    /*
                    if (ex.StackTrace != null)
                    {
                        message.Append("<FONT COLOR=\"#808080\" SIZE=\"10\"><P>");

                        Exception innerException = ex;
                        while (innerException != null)
                        {
                            message.Append("<BR/>");
                            message.Append(innerException.StackTrace.Replace("\n", "<BR/>").Replace("\r", ""));
                            innerException = innerException.InnerException;
                        }
                        message.Append("</P></FONT>");
                    }
                    */

                    message.Append("</HTML></BODY>");

                    this.FinishMessage = message.ToString() ;
                }
            }
            else if (formArgs != null)
            {
                SetDataSource(formArgs.Data);
                this.FormId = formArgs.Form.Id;
                this.RuleSet = formArgs.RuleSet;
                this.StepDescription = formArgs.Description;
                this.EndRule = formArgs.EndRule;
                this.DataStructure = formArgs.DataStructure;
                this.SortSet = formArgs.RefreshSort;
                this.RefreshMethod = formArgs.RefreshMethod;
                this.DataStructureId = (formArgs.SaveDataStructure == null ? formArgs.DataStructure.Id : formArgs.SaveDataStructure.Id);
                this.Parameters = formArgs.Parameters;
                this.Host = formArgs.Engine.Host;
                this.TaskId = formArgs.TaskId;
                this.WorkflowInstanceId = formArgs.Engine.WorkflowInstanceId;
                this.IsFinalForm = formArgs.IsFinalForm;
                this.AllowSave = formArgs.AllowSave;
                this.IsAutoNext = formArgs.IsAutoNext;
                this.IsFirstSaveDone = !formArgs.IsRefreshSuppressedBeforeFirstSave;
                this.ConfirmationRule = formArgs.SaveConfirmationRule;
                this.RefreshPortalAfterSave = formArgs.RefreshPortalAfterSave;
                if (formArgs.Notification == "")
                {
                    this.Notifications.Clear();
                }
                else
                {
                    foreach (string notification in formArgs.Notification.Split("\n".ToCharArray()))
                    {
                        FormNotification fn = new FormNotification();

                        if (notification.StartsWith("! "))
                        {
                            fn.Icon = "warning";
                            fn.Text = notification.Substring(2);
                        }
                        else if (notification.StartsWith("!! "))
                        {
                            fn.Icon = "error";
                            fn.Text = notification.Substring(3);
                        }
                        else
                        {
                            fn.Icon = "info";
                            fn.Text = notification;
                        }

                        this.Notifications.Add(fn);
                    }
                }
            }
            else
            {
                // not form, no exception - workflow is finished
                SetDataSource(null);
                this.FormId = new Guid(Origam.OrigamEngine.ModelXmlBuilders.FormXmlBuilder.WORKFLOW_FINISHED_FORMID);
                this.RuleSet = null;
                this.StepDescription = Resources.WorkflowFinishedTitle;
                this.EndRule = null;
                this.DataStructure = null;
                this.DataStructureId = Guid.Empty;
                this.SortSet = null;
                this.RefreshMethod = null;
                this.Parameters = new Hashtable();
                this.Host = handler.Result.Engine.Host;
                this.TaskId = Guid.Empty;
                this.WorkflowInstanceId = handler.Result.Engine.WorkflowInstanceId;
                this.FinishMessage = (handler.Result.Engine.ResultMessage == "" ? Resources.WorkflowFinished : handler.Result.Engine.ResultMessage);
                this.IsFinalForm = true;
                this.AllowSave = false;
                this.IsAutoNext = false;
                this.ConfirmationRule = null;
                this.Clear();
            }
        }

        private RuleExceptionDataCollection EvaluateEndRule()
        {
            if (this.Data.HasErrors)
            {
                if (log.IsDebugEnabled)
                {
                    log.Debug(Resources.ErrorInForm);
                    log.Debug(DebugClass.ListRowErrors(Data));
                }
                throw new Exception(Resources.ErrorInForm);
            }

            // check end rule
            if (this.EndRule != null)
            {
                if (this.XmlData == null)
                {
                    throw new NullReferenceException("Workflow context does not contain valid data.");
                }

                return this.RuleEngine.EvaluateEndRule(this.EndRule, this.XmlData, this.Parameters);
            }

            return new RuleExceptionDataCollection() ;
        }

        private async System.Threading.Tasks.Task<UIResult> HandleWorkflowNextAsync(IList<string> cachedWorkflowTaskIds)
        {
            RuleExceptionDataCollection results = EvaluateEndRule();
            if (results != null)
            {
                bool isError = false;
                foreach (RuleExceptionData rule in results)
                {
                    if (rule.Severity == RuleExceptionSeverity.High)
                    {
                        isError = true;
                        break;
                    }
                }
                if (isError)
                {
                    throw new RuleException(results);
                }
            }
            WorkflowCallbackHandler handler = new WorkflowCallbackHandler(this.Host, this.WorkflowInstanceId);
            handler.Subscribe();
            this.Host.FinishWorkflowForm(this.TaskId, this.XmlData);
            handler.Event.WaitOne();
            HandleWorkflow(handler);
            UIRequest request = GetRequest(cachedWorkflowTaskIds);
            UIResult result = this.Service.InitUI(request);
            result.WorkflowTaskId = this.TaskId.ToString();
            await System.Threading.Tasks.Task.CompletedTask; //CS1998
            return result;
        }

        private object HandleRefresh()
        {
            if (! this.IsFirstSaveDone) throw new UserOrigamException(Resources.ErrorCannotRefreshFormNotSaved);

            DataSet data = LoadData();

            this.SetDataSource(data);

            return data;
        }

        private async System.Threading.Tasks.Task<UIResult> HandleAbortAsync()
        {
            UserProfile profile = SecurityTools.CurrentUserProfile();

            IPersistenceService ps = ServiceManager.Services.GetService(typeof(IPersistenceService)) as IPersistenceService;
            AbstractMenuItem menuItem = ps.SchemaProvider.RetrieveInstance(typeof(AbstractMenuItem), new ModelElementKey(new Guid(this.Request.ObjectId))) as AbstractMenuItem;

            // abort workflow
            WorkflowCallbackHandler handler = new WorkflowCallbackHandler(this.Host, this.WorkflowInstanceId);
            handler.Subscribe();
            this.Host.AbortWorkflowForm(this.TaskId);
            handler.Event.WaitOne();

            HandleWorkflow(handler);
            await System.Threading.Tasks.Task.CompletedTask; //CS1998
            // call InitUI
            UIRequest request = GetRequest(null);
            return Service.InitUI(request);
        }

        private UIRequest GetRequest(IList<string> cachedWorkflowTaskIds)
        {
            bool die = (this.FormId == new Guid(Origam.OrigamEngine.ModelXmlBuilders.FormXmlBuilder.WORKFLOW_FINISHED_FORMID));
            SessionStore ss;
            if (die && this.ParentSession != null)
            {
                ss = this.ParentSession;
                this.ParentSession.ChildSessions.Remove(this);
                this.ParentSession.ActiveSession = null;
                _disposeAfterAction = true;
            }
            else
            {
                ss = this;
            }
            UIRequest request = new UIRequest();
            request.FormSessionId = ss.Id.ToString();
            request.IsStandalone = ss.Request.IsStandalone;
            request.ObjectId = ss.Request.ObjectId;
            request.IsNewSession = false;
            if (cachedWorkflowTaskIds != null)
            {
                request.IsDataOnly = cachedWorkflowTaskIds.Contains(this.TaskId.ToString());
            }
            return request;
        }
        #endregion
    }
}
