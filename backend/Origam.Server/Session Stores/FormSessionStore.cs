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
using System.Collections;
using System.Xml;

using Origam.Schema.EntityModel;
using Origam.Workbench.Services;
using Origam.Schema.MenuModel;
using Origam.DA;
using Origam.DA.Service;
using core = Origam.Workbench.Services.CoreServices;
using Origam.Schema;
using System.Collections.Generic;
using Origam.Server;
using Origam.Server.Session_Stores;

namespace Origam.Server
{
    public class FormSessionStore : SaveableSessionStore
    {
        private string _delayedLoadingParameterName;
        private bool _isProcessing;
        private FormReferenceMenuItem _menuItem;
        private object _getRowDataLock = new object();
        private XmlDocument _preparedFormXml = null;

        public FormReferenceMenuItem MenuItem => _menuItem;

        public FormSessionStore(IBasicUIService service, UIRequest request, string name, 
            FormReferenceMenuItem menuItem, Analytics analytics)
            : base(service, request, name, analytics)
        {
            _menuItem = menuItem;
            SetMenuProperties();
        }

        public FormSessionStore(IBasicUIService service, UIRequest request, string name, Analytics analytics)
            : base(service, request, name, analytics)
        {
            IPersistenceService ps = ServiceManager.Services.GetService(typeof(IPersistenceService)) as IPersistenceService;
            FormReferenceMenuItem fr = (FormReferenceMenuItem)ps.SchemaProvider.RetrieveInstance(typeof(FormReferenceMenuItem), new ModelElementKey(new Guid(this.Request.ObjectId)));
            _menuItem = fr;
            FormId = _menuItem.ScreenId;
            DataStructureId = _menuItem.Screen.DataSourceId;
            SetMenuProperties();
        }

        private void SetMenuProperties()
        {
            RuleSet = _menuItem.RuleSet;
            Template = _menuItem.DefaultTemplate;
            SortSet = _menuItem.SortSet;
            RefreshAfterSaveType = _menuItem.RefreshAfterSaveType;
            ConfirmationRule = _menuItem.ConfirmationRule;
            RefreshPortalAfterSave = _menuItem.RefreshPortalAfterSave;
        }

        #region Overriden SessionStore Methods
        public override bool SupportsFormXmlAsync => true;

        public override void Init()
        {
            LoadData();
        }

        private void LoadData()
        {
            if(dataRequested)
            {
                LoadDataFxServer();
            }
            else
            {
                PrepareDataCore();
            }
        }

        private void PrepareDataCore()
        {
            var data = InitializeFullStructure(_menuItem.DefaultSet);
            SetDataSource(data);
            SetDelayedLoadingParameter(_menuItem.Method);
            this.IsDelayedLoading = true;
            this.DataListEntity = _menuItem.ListEntity.Name;
        }

        private void LoadDataFxServer()
        {
            DataSet data = null;
            if (this.Request.IsSingleRecordEdit && _menuItem.RecordEditMethod != null)
            {
                data = LoadSingleRecord();
                if (_menuItem.ListEntity != null)
                {
                    SetDataList(null, _menuItem.ListEntity.Name, 
                        _menuItem.ListDataStructure, _menuItem.ListMethod);
                }
                SetDelayedLoadingParameter(_menuItem.RecordEditMethod);
            }
            else if (_menuItem.ListDataStructure == null)
            {
                // all data at once
                data = LoadCompleteData();
            }
            else
            {
                throw new Exception("A screen is lazy loaded but the client requested session data on InitUI " +
                    "call by setting DataRequested=true. Instead the client should set DataRequested=false " +
                    "and call GetRows in order to get the list data and then MasterRecord to load " +
                    "one of the records and GetData to request entity data.");
            }
            if (data != null)
            {
                SetDataSource(data);
            }
        }

        private void SetDelayedLoadingParameter(DataStructureMethod method)
        {
            // set the parameter for delayed data loading - there should be just 1
            DelayedLoadingParameterName = CustomParameterService.GetFirstNonCustomParameter(method);
        }
       

        private string ListPrimaryKeyColumns(DataSet data, string listEntity)
        {
            return GetDataSetBuilder().ListPrimaryKeyColumns(data, listEntity);
        }

        private DataSet LoadCompleteData()
        {
            if (_menuItem.Method != null)
            {
                ResolveFormMethodParameters(_menuItem.Method);
            }

            DataSet data;
            QueryParameterCollection qparams = Request.QueryParameters;
            data = core.DataService.Instance.LoadData(DataStructureId, _menuItem.MethodId, 
                _menuItem.DefaultSetId, _menuItem.SortSetId, null, qparams);
            return data;
        }

        public override void LoadColumns(IList<string> columns)
        {
            QueryParameterCollection qparams = Request.QueryParameters;
            ArrayList finalColumns = new ArrayList();
            ArrayList arrayColumns = new ArrayList();
            foreach (var column in columns)
            {
                if (!DataListLoadedColumns.Contains(column))
                {
                    if (IsColumnArray(DataList.Tables[DataListEntity].Columns[column]))
                    {
                        arrayColumns.Add(column);
                    }
                    else
                    {
                        finalColumns.Add(column);
                    }
                }
            }
            LoadStandardColumns(qparams, finalColumns);
            LoadArrayColumns(this.DataList, this.DataListEntity, qparams, arrayColumns);
        }

        private void LoadArrayColumns(DataSet dataset, string entity,
            QueryParameterCollection qparams, ArrayList arrayColumns)
        {
            lock (_lock)
            {
                foreach (string column in arrayColumns)
                {
                    if (!DataListLoadedColumns.Contains(column))
                    {
                        DataColumn col = dataset.Tables[entity].Columns[column];
                        string relationName = (string)col.ExtendedProperties[Const.ArrayRelation];
                        core.DataService.Instance.LoadData(_menuItem.ListDataStructureId, _menuItem.ListMethodId,
                            Guid.Empty, _menuItem.ListSortSetId, null, qparams, dataset, relationName,
                            null);
                        DataListLoadedColumns.Add(column);
                    }
                }
            }
        }

        private void LoadStandardColumns(QueryParameterCollection qparams, ArrayList finalColumns)
        {
            lock (_lock)
            {
                if (finalColumns.Count == 0)
                {
                    return;
                }
                finalColumns.Add(ListPrimaryKeyColumns(this.DataList, this.DataListEntity));
                DataSet columnData = DatasetTools.CloneDataSet(DataList);
                DataTable listTable = DataList.Tables[DataListEntity];
                core.DataService.Instance.LoadData(_menuItem.ListDataStructureId, _menuItem.ListMethodId,
                    Guid.Empty, _menuItem.ListSortSetId, null, qparams, columnData,
                    this.DataListEntity,
                    string.Join(";", (string[])finalColumns.ToArray(typeof(string))));
                listTable.BeginLoadData();
                try
                {

                    foreach (DataRow columnRow in columnData.Tables[DataListEntity].Rows)
                    {
                        DataRow listRow = listTable.Rows.Find(DatasetTools.PrimaryKey(columnRow));
                        if (listRow != null)
                        {
                            foreach (string column in finalColumns)
                            {
                                listRow[column] = columnRow[column];
                            }
                        }
                    }
                }
                finally
                {
                    listTable.EndLoadData();
                    foreach (string column in finalColumns)
                    {
                        DataListLoadedColumns.Add(column);
                    }
                }
            }
        }

        

        private DataSet LoadSingleRecord()
        {
            DataSet data;
            // We use the RecordEdit filter set for single record editing.
            ResolveFormMethodParameters(_menuItem.RecordEditMethod);
            QueryParameterCollection qparams = Request.QueryParameters;
            data = core.DataService.Instance.LoadData(DataStructureId, _menuItem.RecordEditMethodId,
                _menuItem.DefaultSetId, _menuItem.SortSetId, null, qparams);
            return data;
        }

        private void ResolveFormMethodParameters(DataStructureMethod method)
        {
            // And we have to get the real parameter names from the filters/defaults instead of the "id"
            // set by the client.
            if (this.Request.Parameters.Contains("id"))
            {
                object value = this.Request.Parameters["id"];
                this.Request.Parameters.Clear();

                foreach (DictionaryEntry entry in method.ParameterReferences)
                {
                    this.Request.Parameters[entry.Key] = value;
                }
                foreach (DictionaryEntry entry in DataStructure().ParameterReferences)
                {
                    this.Request.Parameters[entry.Key] = value;
                }
            }
        }

        internal override object Save()
        {
            if (MenuItem.ReadOnlyAccess)
            {
                throw new Exception("Read only session cannot be saved");
            }

            return base.Save();
        }

        public override object ExecuteAction(string actionId)
        {
            if (this.IsProcessing)
            {
                throw new Exception(Resources.ErrorCommandInProgress);
            }

            this.IsProcessing = true;

            try
            {
                switch (actionId)
                {
                    case ACTION_SAVE:
                        object result = Save();
                        //if (this.IsDelayedLoading)
                        //{
                        //    this.CurrentRecordId = null;
                        //}
                        return result;

                    case ACTION_REFRESH:
                        return Refresh();

                    default:
                        throw new ArgumentOutOfRangeException("actionId", actionId, Resources.ErrorContextUnknownAction);
                }
            }
            finally
            {
                this.IsProcessing = false;
            }
        }

        /// <summary>
        /// Called when moving to a new row to load the actual data from the data source
        /// (delayed loading).
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public override ArrayList GetRowData(string entity, object id, bool ignoreDirtyState)
        {
            ArrayList result = new ArrayList();
            lock (_getRowDataLock)
            {
                if (id == null)
                {
                    CurrentRecordId = null;
                    return result;
                }

                DataTable table = GetSessionEntity(entity);

                DataRow row = GetSessionRow(entity, id);

                // for new rows we don't even try to load the data from the database
                if (row == null || row.RowState != DataRowState.Added)
                {
                    if (!ignoreDirtyState && this.Data.HasChanges())
                    {
                        throw new Exception(Resources.ErrorDataNotSavedWhileChangingRow);
                    }

                    this.CurrentRecordId = null;

                    SetDataSource(LoadDataPiece(id));
                }

                this.CurrentRecordId = id;

                DataRow actualDataRow = GetSessionRow(entity, id);
                UpdateListRow(actualDataRow);

                ChangeInfo ci = GetChangeInfo(null, actualDataRow, 0);
                result.Add(ci);

                if (actualDataRow.RowState == DataRowState.Unchanged)
                {
                    result.Add(ChangeInfo.SavedChangeInfo());
                }
            }

            return result;
        } 
        
        public override ChangeInfo GetRow(string entity, object id)
        {
            lock (_getRowDataLock)
            {
                if (id == null)
                {
                    CurrentRecordId = null;
                    return null;
                }
                DataRow row = GetSessionRow(entity, id);
                return GetChangeInfo(null, row, Operation.Update);
            }
        }
        
        public override void RevertChanges()
        {
            lock (_getRowDataLock)
            {
                try
                {
                    UnregisterEvents();
                    Data.RejectChanges();
                }
                finally
                {
                    RegisterEvents();
                }
            }
        }

        public override ArrayList GetData(string childEntity, object parentRecordId, object rootRecordId)
        {
            // check validity of the request
            if (!rootRecordId.Equals(this.CurrentRecordId))
            {
                // we do not hold the data anymore, we throw-out the request
                return new ArrayList();
            }
            DataTable childTable = GetSessionEntity(childEntity);
            ArrayList result = new ArrayList();
            if (childTable.ParentRelations.Count == 0)
            {
                throw new Exception("Requested entity " + childEntity + " has no parent relations. Cannot load child records.");
            }
            DataRelation parentRelation = childTable.ParentRelations[0];
            // get parent row again (the one before was most probably loaded from the list
            // now we have it in the cache
            DataRow parentRow = GetSessionRow(parentRelation.ParentTable.TableName, parentRecordId);
            if (parentRow == null)
            {
                throw new ArgumentOutOfRangeException($"Parent record id " +
                    $"{parentRecordId} not found in " +
                    $"{parentRelation.ParentTable.TableName} - " +
                    $"parent of {childEntity}.");
            }
            // get the requested entity data
            string[] columns = GetColumnNames(childTable);
            foreach (DataRow r in parentRow.GetChildRows(parentRelation.RelationName))
            {
                result.Add(GetRowData(r, columns));
            }
            return result;
        }

        internal override void OnNewRecord(string entity, object id)
        {
            if (IsLazyLoadedEntity(entity))
            {
                this.CurrentRecordId = null;
                
                // convert guid to string, because if we get data from Fluorine,
                // it would come as a string, so we would not be able to compare later
                if (id is Guid)
                {
                    id = id.ToString();
                }

                this.CurrentRecordId = id;
            }
        }

        public override bool HasChanges()
        {
            return !MenuItem.ReadOnlyAccess && Data != null && Data.HasChanges();
        }

        public override IList RestoreData(object recordId)
        {
            ArrayList result = new ArrayList();

            // get the original row and return it to the client, so it updates to 
            // the original state
            DataRow originalRow = this.GetSessionRow(this.DataListEntity, recordId);

            if (originalRow.RowState == DataRowState.Added)
            {
                result.AddRange(this.DeleteObject(originalRow.Table.TableName, recordId));
            }
            else
            {
                this.CurrentRecordId = null;
                
                // update the values from database
                this.GetRowData(this.DataListEntity, recordId, true);
                
                // get the loaded data
                originalRow = this.GetSessionRow(this.DataListEntity, recordId);

                // return the loaded data
                result.Add(GetChangeInfo(null, originalRow, 0));
            }

            if(recordId.Equals(this.CurrentRecordId))
            {
                this.CurrentRecordId = null;
            }

            // add SAVED operation in the end, since we ignored the data and start with a fresh copy
            result.Add(ChangeInfo.SavedChangeInfo());

            return result;
        }

        private DataSet LoadDataPiece(object parentId)
        {
            return core.DataService.Instance.LoadData(DataStructureId, _menuItem.MethodId, 
                _menuItem.DefaultSetId, Guid.Empty, null, 
                DelayedLoadingParameterName, parentId);
        }

        public override XmlDocument GetFormXml()
        {
            // asynchronously prepared xml
            if (_preparedFormXml != null)
            {
                XmlDocument result = _preparedFormXml;
                _preparedFormXml = null;
                return result;
            }
            XmlDocument formXml = OrigamEngine.ModelXmlBuilders.FormXmlBuilder.GetXml(new Guid(this.Request.ObjectId)).Document;
            XmlNodeList list = formXml.SelectNodes("/Window");
            XmlElement windowElement = list[0] as XmlElement;
            // The SuppressSave attribute causes the Save button to disappear.
            // It should not be set to true if there is at least one editable field on the screen. The final result can be
            // determined only after the whole screen xml has been created.
            if (windowElement.GetAttribute("SuppressSave") == "true" && 
                formXml.SelectNodes("//Property[@ReadOnly='false']")?.Count > 0)
            {
                windowElement.SetAttribute("SuppressSave", "false");
            }
            if (windowElement.GetAttribute("SuppressSave") == "true")
            {
                this.SuppressSave = true;
            }
            return formXml;
        }

        public override void PrepareFormXml()
        {
            if (log.IsDebugEnabled)
            {
                log.Debug("Preparing XML...");
            }
            _preparedFormXml = GetFormXml();
            if (log.IsDebugEnabled)
            {
                log.Debug("XML prepared...");
            }
        }

        private object Refresh()
        {
            object currentRecordId = this.CurrentRecordId;

            try
            {
                this.IsProcessing = true;
                this.CurrentRecordId = null;
                this.Clear();
            }
            finally
            {
                this.IsProcessing = false;
            }

            LoadData();

            // load current record again so CurrentRecordId remains set
            // this is important e.g. for paged data loading so we know
            // for which record initialPage should be returned
            if (currentRecordId != null && this.DataListEntity != null)
            {
                try
                {
                    GetRowData(this.DataListEntity, currentRecordId, false);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // in case the original current record does not exist in the
                    // context anymore after refresh, we do not set the current
                    // record id
                }
            }

            if (this.DataList != null)
            {
                return this.DataList;
            }
            else
            {
                return this.Data;
            }
        }
        #endregion

        #region Properties

        public string DelayedLoadingParameterName
        {
            get { return _delayedLoadingParameterName; }
            set { _delayedLoadingParameterName = value; }
        }

        public bool IsProcessing
        {
            get { return _isProcessing; }
            set { _isProcessing = value; }
        }
        #endregion

        public override void OnDispose()
        {
            base.OnDispose();

            this.CurrentRecordId = null;
        }
    }
}
