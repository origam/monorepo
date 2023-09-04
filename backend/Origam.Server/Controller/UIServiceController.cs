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

using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Origam.DA;
using Origam.DA.Service;
using Origam.OrigamEngine.ModelXmlBuilders;
using Origam.Schema.EntityModel;
using Origam.Schema.LookupModel;
using Origam.Schema.MenuModel;
using Origam.Services;
using Origam.Workbench.Services;
using Origam.Workbench.Services.CoreServices;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Xml;
using IdentityServer4;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using Origam.Extensions;
using Origam.Server.Configuration;
using Origam.Server.Model.Search;
using Origam.Server.Model.UIService;
using Origam.Workbench;
using Origam.Service.Core;
using IdentityServerConstants = IdentityServer4.IdentityServerConstants;

namespace Origam.Server.Controller
{
    [Authorize(IdentityServerConstants.LocalApi.PolicyName)]
    [ApiController]
    [Route("internalApi/[controller]")]
    public class UIServiceController : AbstractController
    {
        private readonly IStringLocalizer<SharedResources> localizer;
        private readonly ClientFilteringConfig clientFilteringConfig;
        private readonly IDataLookupService lookupService;
        private readonly RequestLocalizationOptions localizationOptions;
        private readonly CustomAssetsConfig customAssetsConfig;
        private readonly HtmlClientConfig htmlClientConfig;
        private readonly ChatConfig chatConfig;

        public UIServiceController(
            SessionObjects sessionObjects,
            IStringLocalizer<SharedResources> localizer,
            ILogger<AbstractController> log,
            IOptions<RequestLocalizationOptions> localizationOptions,
            IOptions<CustomAssetsConfig> customAssetsOptions,
            IOptions<ClientFilteringConfig> filteringConfig,
            IOptions<HtmlClientConfig> htmlClientConfigOptions,
            IOptions<ChatConfig> chatConfigOptions)
            : base(log, sessionObjects)
        {
            this.localizer = localizer;
            this.clientFilteringConfig = filteringConfig.Value;
            this.localizationOptions = localizationOptions.Value;
            customAssetsConfig = customAssetsOptions.Value;
            htmlClientConfig = htmlClientConfigOptions.Value;
            lookupService
                = ServiceManager.Services.GetService<IDataLookupService>();
            chatConfig = chatConfigOptions.Value;
        }
        #region Endpoints
        [HttpGet("[action]")]
        // ReSharper disable once UnusedParameter.Global
        public IActionResult InitPortal()
        {
            Analytics.Instance.Log("UI_INIT");
            return RunWithErrorHandler(() =>
            {
                PortalResult result = sessionObjects.UIService.InitPortal(4);
                AddConfigData(result);
                return Ok(result);
            });
        }
        [AllowAnonymous]
        [HttpGet("[action]")]
        public IActionResult DefaultLocalizationCookie()
        {
            return RunWithErrorHandler(() =>
            {
                var cultureProvider = localizationOptions.RequestCultureProviders
                    .OfType<OrigamCookieRequestCultureProvider>().First();
                string localizationCookie = cultureProvider.MakeCookieValue(
                    localizationOptions.DefaultRequestCulture);
                return Ok(localizationCookie);
            });
        }
        [HttpPost("[action]")]
        public IActionResult InitUI([FromBody]UIRequest request)
        {
            return RunWithErrorHandler(() => 
                Ok(sessionObjects.UIManager.InitUI(
                    request: request,
                    addChildSession: false,
                    parentSession: null,
                    basicUIService: sessionObjects.UIService)));
        }
        [HttpPost("[action]")]
        public IActionResult DestroyUI([FromBody]DestroyInput input)
        {
            return RunWithErrorHandler(() =>
            {
                sessionObjects.UIService.DestroyUI(input.FormSessionId);
                return Ok();
            });
        }
        [HttpGet("[action]/{sessionFormIdentifier:guid}")]
        public IActionResult RefreshData(Guid sessionFormIdentifier)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.RefreshData(
                    sessionFormIdentifier, localizer)));
        }
        [HttpPost("[action]")]
        public IActionResult RestoreData([FromBody]RestoreDataInput input)
        {
            return RunWithErrorHandler(()
                => Ok(sessionObjects.UIService.RestoreData(input)));
        }
        [HttpGet("[action]/{sessionFormIdentifier:guid}")]
        public IActionResult SaveDataQuery(Guid sessionFormIdentifier)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.SaveDataQuery(
                    sessionFormIdentifier)));
        }
        [HttpGet("[action]/{sessionFormIdentifier:guid}")]
        public IActionResult SaveData(Guid sessionFormIdentifier)
        {
            return RunWithErrorHandler(() =>
            {
                var ruleExceptionData = sessionObjects.UIService.SaveDataQuery(sessionFormIdentifier);
                var errors = ruleExceptionData
                    .Cast<RuleExceptionData>()
                    .Where(data => data.Severity == RuleExceptionSeverity.High)
                    .Select(data =>$"Entity: {data.EntityName}, Field: {data.FieldName}, Message: {data.Message}, Severity: {data.Severity}")
                    .ToList();
                if (errors.Count > 0)
                {
                    return StatusCode(409, string.Join("\n", errors));
                }
                return Ok(sessionObjects.UIService.SaveData(
                    sessionFormIdentifier));
            });
        }
        [HttpPost("[action]")]
        public IActionResult RevertChanges([FromBody]RevertChangesInput input)
        {
            return RunWithErrorHandler(() =>
            {
                sessionObjects.UIService.RevertChanges(input);
                return Ok();
            });
        }        
        [HttpPost("[action]")]
        public IActionResult MasterRecord([FromBody]MasterRecordInput input)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.GetRowData(input)));
        }
        [HttpPost("[action]")]
        public IActionResult GetData([FromBody]GetDataInput input)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.GetData(input)));
        }
        [HttpPost("[action]")]
        public IActionResult RowStates([FromBody]RowStatesInput input)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.RowStates(input)));
        }
        [HttpPost("[action]")]
        public IActionResult CreateObject(
            [FromBody][Required]CreateObjectInput input)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.CreateObject(input)));
        }
        [HttpPost("[action]")]
        public IActionResult CopyObject(
            [FromBody][Required]CopyObjectInput input)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.CopyObject(input)));
        }
        [HttpPost("[action]")]
        public IActionResult UpdateObject(
            [FromBody][Required]UpdateObjectInput input)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.UpdateObject(input)));
        }
        [HttpPost("[action]")]
        public IActionResult DeleteObject(
            [FromBody][Required]DeleteObjectInput input)
        {
            //todo: handle deleting non existing objects
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.DeleteObject(input)));
        }        
        [HttpPost("[action]")]
        public IActionResult DeleteObjectInOrderedList(
            [FromBody][Required]DeleteObjectInOrderedListInput input)
        {
            //todo: handle deleting non existing objects
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.DeleteObjectInOrderedList(input)));
        }
        [HttpPost("[action]")]
        public IActionResult ExecuteActionQuery(
            [FromBody][Required]ExecuteActionQueryInput input)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.ExecuteActionQuery(input)));
        }
        [HttpPost("[action]")]
        public IActionResult ExecuteAction(
            [FromBody][Required]ExecuteActionInput input)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.ExecuteAction(input)));
        }
        [HttpPost("[action]")]
        public IActionResult GetLookupLabels(
            [FromBody][Required]LookupLabelsInput input)
        {
            return RunWithErrorHandler(() =>
            {
                // todo: unify approach
                var checkResult = CheckLookup(input);
                if(checkResult != null)
                {
                    return checkResult;
                }
                var labelDictionary = GetLookupLabelsInternal(input);
                return Ok(labelDictionary);
            });
        }
        [HttpPost("[action]")]
        public IActionResult GetLookupCacheDependencies(
            [FromBody][Required]GetLookupCacheDependenciesInput input)
        {
            return RunWithErrorHandler(() =>
            {
                var dependencies 
                    = new Dictionary<string, object>(input.LookupIds.Length);
                foreach(var lookupId in input.LookupIds)
                {
                    dependencies.Add((string)lookupId, 
                        GetLookupCacheDependencies((string)lookupId));
                }
                return Ok(dependencies);
            });
        }
        [HttpGet("[action]")]
        public IActionResult WorkQueueList()
        {
            return RunWithErrorHandler(() => 
                Ok(ServerCoreUIService.WorkQueueList(localizer)));
        }
        [HttpPost("[action]")]
        public IActionResult ResetScreenColumnConfiguration(
            [FromBody][Required]ResetScreenColumnConfigurationInput input)
        {
            return RunWithErrorHandler(() =>
            {
                sessionObjects.UIService.ResetScreenColumnConfiguration(input);
                return Ok();
            });
        }        
        [HttpPost("[action]")]
        public IActionResult SaveObjectConfig(
            [FromBody][Required]SaveObjectConfigInput input)
        {
            return RunWithErrorHandler(() =>
            {
                sessionObjects.UIService.SaveObjectConfig(input);
                return Ok();
            });
        }
        [HttpPost("[action]")]
        public IActionResult SaveSplitPanelConfig(
            [FromBody][Required]SaveSplitPanelConfigInput input)
        {
            return RunWithErrorHandler(() =>
            {
                ServerCoreUIService.SaveSplitPanelConfig(input);
                return Ok();
            });
        }
        [HttpPost("[action]")]
        public IActionResult GetLookupLabelsEx([FromBody]LookupLabelsInput[] inputs)
        {
            return RunWithErrorHandler(() =>
            {
                var result = new Dictionary<Guid, Dictionary<object, string>>();
                foreach(var input in inputs)
                {
                    var checkResult = CheckLookup(input);
                    if(checkResult != null)
                    {
                        return checkResult;
                    }
                    var labelDictionary = GetLookupLabelsInternal(input);
                    result.Add(input.LookupId, labelDictionary);
                }
                return Ok(result);
            });
        }
        [HttpPost("[action]")]
        public IActionResult GetLookupList([FromBody]LookupListInput input)
        {
            //todo: implement GetFilterLookupList
            return LookupListInputToRowData(input)
                .Bind(rowData => GetLookupRows(input, rowData))
                .Map(ToActionResult)
                .Finally(UnwrapReturnValue);
        }
        [HttpPost("[action]")]
        public IActionResult GetRows([FromBody]GetRowsInput input)
        {
            return RunWithErrorHandler(() =>
            {
                var sessionStore = sessionObjects.SessionManager.GetSession(
                    input.SessionFormIdentifier);
                if (sessionStore is WorkQueueSessionStore workQueueSessionStore)
                {
                    return WorkQueueGetRowsGetRowsQuery(input, workQueueSessionStore)
                        .Bind(dataStructureQuery=>
                            ExecuteDataReader(
                                dataStructureQuery: dataStructureQuery,
                                methodId: input.MenuId))
                        .Map(ToActionResult)
                        .Finally(UnwrapReturnValue);
                }
                return EntityIdentificationToEntityData(input)
                    .Bind(entityData => GetRowsGetQuery(input, entityData))
                    .Bind(dataStructureQuery=>
                        ExecuteDataReader(
                            dataStructureQuery: dataStructureQuery,
                            methodId: input.MenuId))
                    .Map(ToActionResult)
                    .Finally(UnwrapReturnValue);
            });
        }

        [HttpPost("[action]")]
        public IActionResult GetRow([FromBody]MasterRecordInput input)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.GetRow(input)));
        }

        [HttpPost("[action]")]
        public IActionResult GetAggregations([FromBody]GetGroupsAggregations input)
        {
            return RunWithErrorHandler(() => { 
               var sessionStore = sessionObjects.SessionManager.GetSession(
                    input.SessionFormIdentifier);
                if (sessionStore is WorkQueueSessionStore workQueueSessionStore)
                {
                   return WorkQueueGetRowsGetAggregationQuery(input, workQueueSessionStore)
                        .Bind(ExecuteDataReaderGetPairs)
                        .Bind(ExtractAggregationList)
                        .Map(ToActionResult)
                        .Finally(UnwrapReturnValue);
                }
                return EntityIdentificationToEntityData(input)
                    .Bind(entityData => GetRowsGetAggregationQuery(input, entityData))                    
                    .Bind(ExecuteDataReaderGetPairs)
                    .Bind(ExtractAggregationList)
                    .Map(ToActionResult)
                    .Finally(UnwrapReturnValue);
            });
        }

        [HttpPost("[action]")]
        public IActionResult GetGroups([FromBody]GetGroupsInput input)
        {
            return RunWithErrorHandler(() =>
            {
                return EntityIdentificationToEntityData(input)
                    .Bind(entityData => GetRowsGetGroupQuery(input, entityData))                    
                    .Bind(ExecuteDataReaderGetPairs)
                    .Map(ToActionResult)
                    .Finally(UnwrapReturnValue);
            });
        }
        [HttpPut("[action]")]
        public IActionResult Row([FromBody]UpdateRowInput input)
        {
            return FindItem<FormReferenceMenuItem>(input.MenuId)
                .Bind(Authorize)
                .Bind(menuItem => GetEntityData(
                    input.DataStructureEntityId, 
                    (FormReferenceMenuItem)menuItem))
                .Bind(CheckEntityBelongsToMenu)
                .Bind(entityData => 
                    GetRow(
                        dataService,
                        entityData.Entity, 
                        input.DataStructureEntityId,
                        Guid.Empty,
                        input.RowId))
                .Tap(rowData => FillRow(rowData, input.NewValues))
                .Map(rowData => SubmitChange(rowData, Operation.Update))
                .Finally(UnwrapReturnValue);
        }
        [HttpPost("[action]")]
        public IActionResult Row([FromBody]NewRowInput input)
        {
            return FindItem<FormReferenceMenuItem>(input.MenuId)
                .Bind(Authorize)
                .Bind(menuItem => GetEntityData(
                    input.DataStructureEntityId, 
                    (FormReferenceMenuItem)menuItem))
                .Bind(CheckEntityBelongsToMenu)
                .Map(entityData => MakeEmptyRow(entityData.Entity))
                .Tap(rowData => FillRow(input, rowData))
                .Map(rowData => SubmitChange(rowData, Operation.Create))
                .Finally(UnwrapReturnValue);
        }
        [HttpPost("[action]")]
        public IActionResult NewEmptyRow([FromBody]NewEmptyRowInput input)
        {
            return FindItem<FormReferenceMenuItem>(input.MenuId)
                .Bind(Authorize)
                .Bind(menuItem =>
                    GetEntityData(input.DataStructureEntityId,
                        (FormReferenceMenuItem)menuItem))
                .Bind(CheckEntityBelongsToMenu)
                .Map(entityData => MakeEmptyRow(entityData.Entity))
                .Map(PrepareNewRow)
                .Map(rowData => SubmitChange(rowData, Operation.Create))
                .Finally(UnwrapReturnValue);
        }
        [HttpDelete("[action]")]
        public IActionResult Row([FromBody]DeleteRowInput input)
        {
            return FindItem<FormReferenceMenuItem>(input.MenuId)
                .Bind(Authorize)
                .Bind(menuItem => GetEntityData(
                    input.DataStructureEntityId, 
                    (FormReferenceMenuItem)menuItem))
                .Bind(CheckEntityBelongsToMenu)
                .Bind(entityData => GetRow(
                    dataService,
                    entityData.Entity,
                    input.DataStructureEntityId,
                    Guid.Empty,
                    input.RowIdToDelete))
                .Map(rowData =>
                {
                    rowData.Row.Delete();
                    return SubmitDelete(rowData);
                })
                .Map(ThrowAwayReturnData)
                .Finally(UnwrapReturnValue);
        }
        [HttpGet("[action]/{sessionFormIdentifier}")]
        public IActionResult WorkflowNextQuery(Guid sessionFormIdentifier)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.WorkflowNextQuery(
                    sessionFormIdentifier)));
        }
        [HttpPost("[action]")]
        public IActionResult WorkflowNext(
            [FromBody][Required]WorkflowNextInput input)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.WorkflowNext(input)));
        }
        [HttpGet("[action]/{sessionFormIdentifier}")]
        public IActionResult WorkflowAbort(Guid sessionFormIdentifier)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.WorkflowAbort(
                    sessionFormIdentifier)));
        }
        [HttpGet("[action]/{sessionFormIdentifier}")]
        public IActionResult WorkflowRepeat(Guid sessionFormIdentifier)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.WorkflowRepeat(
                    sessionFormIdentifier, localizer).Result));
        }
        [HttpPost("[action]")]
        public IActionResult AttachmentCount(
            [FromBody][Required]AttachmentCountInput input)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.AttachmentCount(input)));
        }
        [HttpPost("[action]")]
        public IActionResult AttachmentList(
            [FromBody][Required]AttachmentListInput input)
        {
            return RunWithErrorHandler(() 
                => Ok(sessionObjects.UIService.AttachmentList(input)));
        }
        [HttpPost("[action]")]
        public IActionResult GetRecordTooltip(
            [FromBody]GetRecordTooltipInput input)
        {
            return AmbiguousInputToRowData(input, dataService)
                .Map(RowDataToRecordTooltip)
                .Finally(result =>
                {
                    if (result.IsSuccess)
                    {
                        return result.Value;
                    }
                    if (result.Error is NotFoundObjectResult notFoundResult)
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.LoadXml("<tooltip title=\"'Error'\">"+
                                        $"<cell type=\"text\" x=\"0\" y=\"1\" height=\"1\" width=\"1\">Error</cell>"+
                                        $"<cell type=\"text\" x=\"0\" y=\"2\" height=\"1\" width=\"1\">{notFoundResult.Value}</cell>"+
                                    "</tooltip>");
                        return Ok(doc);
                    }
                    return result.Error;
                });
        }
        [HttpGet("[action]")]
        public IActionResult GetNotificationBoxContent()
        {
            XmlWriterSettings xmlWriterSettings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };
            return Ok(ServerCoreUIService.NotificationBoxContent().ToBeautifulString(xmlWriterSettings));
        }
        [HttpPost("[action]")]
        public IActionResult GetAudit([FromBody]GetAuditInput input)
        {
            return AmbiguousInputToEntityId(input)
                .Map(entityId => 
                    GetAuditLog(entityId, input.RowId))
                .Finally(UnwrapReturnValue);
        }
        [HttpPost("[action]")]
        public IActionResult SaveFavorites(
            [FromBody][Required]SaveFavoritesInput input)
        {
            return RunWithErrorHandler(() =>
            {
                ServerCoreUIService.SaveFavorites(input);
                return Ok();
            });
        }
        [HttpGet("[action]/{sessionFormIdentifier:guid}")]
        public IActionResult PendingChanges(Guid sessionFormIdentifier)
        {
            return RunWithErrorHandler(() => Ok(
                sessionObjects.UIService.GetPendingChanges(
                    sessionFormIdentifier)));
        }
        [HttpPost("[action]")]
        public IActionResult Changes([FromBody]ChangesInput input)
        {
            return RunWithErrorHandler(() => Ok(
                sessionObjects.UIService.GetChanges(input)));
        }
        [HttpPost("[action]")]
        public IActionResult SaveFilter([FromBody]SaveFilterInput input)
        {
            return FindEntity(input.DataStructureEntityId)
                .Bind(dataStructureEntity
                    => ServerCoreUIService.SaveFilter(dataStructureEntity, input))
                .Map(filterId => ToActionResult(filterId))
                .Finally(UnwrapReturnValue);

        }
        [HttpPost("[action]")]
        public IActionResult DeleteFilter([FromBody]DeleteFilterInput deleteFilterInput)
        {
            return RunWithErrorHandler(() =>
            {
                ServerCoreUIService.DeleteFilter(deleteFilterInput.FilterId);
                return Ok();
            });
        }
        [HttpPost("[action]")]
        public IActionResult SetDefaultFilter(
            [FromBody]SetDefaultFilterInput input)
        {
            return FindEntity(input.DataStructureEntityId)
                .Tap(dataStructureEntity
                    => sessionObjects.UIService.SetDefaultFilter(
                        input, dataStructureEntity))
                .Map(ToActionResult)
                .Map(ThrowAwayReturnData)
                .Finally(UnwrapReturnValue);
        }
        [HttpPost("[action]")]
        public IActionResult ResetDefaultFilter(
            [FromBody]ResetDefaultFilterInput input)
        {
            return RunWithErrorHandler(() =>
            {
                sessionObjects.UIService.ResetDefaultFilter(input);
                return Ok();
            });
        }
        [HttpGet("[action]/{menuId}")]
        public IActionResult ReportFromMenu(Guid menuId)
        {
            return RunWithErrorHandler(() =>
            {
                return FindItem<ReportReferenceMenuItem>(menuId)
                    .Bind(Authorize)
                    .Map(menuItem => sessionObjects.UIService.ReportFromMenu(
                        menuItem.Id))
                    .Map(ToActionResult)
                    .Finally(UnwrapReturnValue);
            });
        }
        [HttpPost("[action]")]
        public IActionResult GetMenuId([FromBody]GetMenuInput input)
        {
            return RunWithErrorHandler(() => Ok(GetMenuId(
                lookupId: input.LookupId, 
                ReferenceId: input.ReferenceId))
            );
        }
        
        [HttpPost("[action]")]
        public IActionResult GetFilterListValues(
            [FromBody] GetFilterListValuesInput input)
        {
            return RunWithErrorHandler(() =>
            {
                var sessionStore = sessionObjects.SessionManager.GetSession(
                    input.SessionFormIdentifier);
                if (sessionStore is WorkQueueSessionStore workQueueSessionStore)
                {
                    return GetFilterListValuesQuery(
                            input,
                            GetWorkQueueEntityData(workQueueSessionStore))
                        .Map(queryData =>
                        {
                            var query = queryData.DataStructureQuery;
                            query.MethodId = workQueueSessionStore.WQClass
                                .WorkQueueStructureUserListMethodId;
                            query.SortSetId = workQueueSessionStore.WQClass
                                .WorkQueueStructureSortSetId;
                            query.DataSourceId = workQueueSessionStore
                                .WQClass.WorkQueueStructureId;
                            query.Parameters.Add(new QueryParameter(
                                "WorkQueueEntry_parWorkQueueId", sessionStore.Request.ObjectId));
                            return query;
                        })
                        .Bind(ExecuteDataReaderGetPairs)
                        .Bind(StreamlineFilterListValues)
                        .Map(ToActionResult)
                        .Finally(UnwrapReturnValue);
                }
                return EntityIdentificationToEntityData(input)
                    .Bind(entityData => GetFilterListValuesQuery(
                        input, entityData))
                    .Bind(queryData =>
                        AddMethodAndSource(
                            queryData.SessionFormIdentifier, 
                            Guid.Empty, 
                            queryData.EntityData,
                            queryData.DataStructureQuery))
                    .Bind(ExecuteDataReaderGetPairs)
                    .Bind(StreamlineFilterListValues)
                    .Map(ToActionResult)
                    .Finally(UnwrapReturnValue);
            });
        }
        #endregion
        
        
        private EntityData GetWorkQueueEntityData(WorkQueueSessionStore workQueueSessionStore)
        {
            ArrayList entities = workQueueSessionStore.WQClass
                .WorkQueueStructure
                .Entities;
            var structureEntity = entities
                .Cast<DataStructureEntity>()
                .FirstOrDefault(entity => entity.Name == workQueueEntity);
            if (entities.Count != 1 || structureEntity == null)
            {
                throw new ArgumentException($"WorkQueueStructure {workQueueSessionStore.WQClass.WorkQueueStructure.Id} must contain exactly one {nameof(DataStructureEntity)} called \"{workQueueEntity}\"");
            }

            return new EntityData
            {
                Entity = structureEntity,
                MenuItem = null
            };
        }
        
        private string GetMenuId(Guid lookupId, Guid ReferenceId)
        {
            return ServiceManager.Services
                .GetService<IDataLookupService>()
                .GetMenuBinding(lookupId, ReferenceId)
                .MenuId;
        }
        private Result<object, IActionResult> ExtractAggregationList(IEnumerable<Dictionary<string, object>> readerResult)
        {
            var rowList = readerResult?.ToList();
            if(rowList == null || rowList.Count == 0)
            {
                return Result.Ok<object, IActionResult>(new List<Dictionary<string, object>>());
            }

            var firstRow = rowList[0];
            if(firstRow == null || !firstRow.ContainsKey("aggregations"))
            {
                return Result.Ok<object, IActionResult>(new List<Dictionary<string, object>>());
            }

            return Result.Ok<object, IActionResult>(firstRow["aggregations"]);
        }
        private Result<IEnumerable<object>, IActionResult> StreamlineFilterListValues(
            IEnumerable<IEnumerable<KeyValuePair<string, object>>> fullReaderResult)
        {
            var streamlinedList = new List<object>();
            foreach(var entry in fullReaderResult)
            {
                streamlinedList.Add(entry.First().Value);
            }
            return Result.Ok<IEnumerable<object>, IActionResult>(streamlinedList);
        }
        private Dictionary<object, string> GetLookupLabelsInternal(
            LookupLabelsInput input)
        {
            var labelDictionary = input.LabelIds.ToDictionary(
                    id => id,
                    id =>
                    {
                        object lookupResult
                            = lookupService.GetDisplayText(
                                input.LookupId, id, false, true, null);
                        return lookupResult is decimal result
                            ? result.ToString("0.#")
                            : lookupResult.ToString();
                    });
            return labelDictionary;
        }
        private IList<string> GetLookupCacheDependencies(string lookupId)
        {
            // TODO: Later we can implement multiple dependencies e.g. for entities based on views where we
            // could name all the source entities/tables on which the view is dependent so even view based
            // lookups could be handled correctly and reset when source table change.
            var result = new List<string>();
            var persistenceService 
                = ServiceManager.Services.GetService<IPersistenceService>();
            var dataServiceDataLookup 
                = persistenceService.SchemaProvider
                    .RetrieveInstance<DataServiceDataLookup>(
                        new Guid(lookupId));
            var datasetGenerator = new DatasetGenerator(true);
            var comboListDataset = datasetGenerator.CreateDataSet(
                dataServiceDataLookup.ListDataStructure);
            var comboListTable = comboListDataset.Tables[
                ((DataStructureEntity) dataServiceDataLookup.ListDataStructure
                    .ChildItemsByType(DataStructureEntity.CategoryConst)[0])
                .Name];
            var tableName = FormXmlBuilder.DatabaseTableName(comboListTable);
            if(tableName != null)
            {
                result.Add(tableName);
            }
            return result;
        }
        private IActionResult CheckLookup(LookupLabelsInput input)
        {
            if(input.MenuId == Guid.Empty)
            {
                SecurityTools.CurrentUserProfile();
            }
            else
            {
                var menuResult = FindItem<FormReferenceMenuItem>(
                    input.MenuId)
                    .Bind(Authorize)
                    .Bind(menuItem
                        => CheckLookupIsAllowedInMenu(
                            (FormReferenceMenuItem)menuItem, input.LookupId));
                if(menuResult.IsFailure)
                {
                    return menuResult.Error;
                }
            }
            return null;
        }
      
        private Result<FormReferenceMenuItem, IActionResult> CheckLookupIsAllowedInMenu(
            FormReferenceMenuItem menuItem, Guid lookupId)
        {
            if(!MenuLookupIndex.HasDataFor(menuItem.Id))
            {
                var xmlOutput = FormXmlBuilder.GetXml(menuItem.Id);
                MenuLookupIndex.AddIfNotPresent(
                    menuItem.Id, xmlOutput.ContainedLookups);
            }
            return MenuLookupIndex.IsAllowed(menuItem.Id, lookupId)
                ? Result.Success<FormReferenceMenuItem, IActionResult>(menuItem)
                : Result.Failure<FormReferenceMenuItem, IActionResult>(
                    BadRequest("Lookup is not referenced in any entity in the Menu item"));
        }
        private IEnumerable<object[]> GetRowData(
            LookupListInput input, DataTable dataTable)
        {
            var lookup = FindItem<DataServiceDataLookup>(input.LookupId).Value;
            if(lookup.IsFilteredServerside || string.IsNullOrEmpty(input.SearchText))
            {
                return dataTable.Rows
                    .Cast<DataRow>()
                    .Select(row => GetColumnValues(row, input.ColumnNames));
            }
            var columnNamesWithoutPrimaryKey = FilterOutPrimaryKey(
                input.ColumnNames, dataTable.PrimaryKey);
            return dataTable.Rows
                .Cast<DataRow>()
                .Where(row => Filter(row, columnNamesWithoutPrimaryKey, 
                    input.SearchText))
                .Select(row => GetColumnValues(row, input.ColumnNames));
        }
        private static object[] GetColumnValues(
            DataRow row, IEnumerable<string> columnNames)
        {
            return columnNames.Select(colName => row[colName]).ToArray();
        }
        private static string[] FilterOutPrimaryKey(
            IEnumerable<string> columnNames, DataColumn[] primaryKey)
        {
            return columnNames
                .Where(columnName => primaryKey.All(
                    dataColumn => dataColumn.ColumnName != columnName))
                .ToArray();
        }
        private static bool Filter(
            DataRow row, IEnumerable<string> columnNames, string likeParameter)
        {
            return columnNames
                .Select(colName => row[colName])
                .Any(colValue => 
                    colValue.ToString().Contains(
                        likeParameter, 
                        StringComparison.InvariantCultureIgnoreCase));
        }
        private Result<IEnumerable<object[]>,IActionResult> GetLookupRows(
            LookupListInput input, RowData rowData)
        {
            var internalRequest = new LookupListRequest
            {
                LookupId = input.LookupId,
                FieldName = input.Property,
                CurrentRow = rowData.Row,
                ShowUniqueValues = input.ShowUniqueValues,
                SearchText = input.SearchText,
                PageSize = input.PageSize,
                PageNumber = input.PageNumber,
                ParameterMappings = DictionaryToHashtable(input.Parameters)
            };
            var dataTable = lookupService.GetList(internalRequest);
            return AreColumnNamesValid(input, dataTable)
                ? Result.Success<IEnumerable<object[]>, IActionResult>(
                    GetRowData(input, dataTable))
                : Result.Failure<IEnumerable<object[]>, IActionResult>(
                    BadRequest("Some of the supplied column names are not in the table."));
        }
        private Result<RowData, IActionResult> LookupListInputToRowData(
            LookupListInput input)
        {
            if(input.SessionFormIdentifier == Guid.Empty)
            {
                return FindItem<FormReferenceMenuItem>(input.MenuId)
                    .Bind(Authorize)
                    .Bind(menuItem => CheckLookupIsAllowedInMenu(
                        (FormReferenceMenuItem)menuItem, input.LookupId))
                    .Bind(menuItem => GetEntityData(
                        input.DataStructureEntityId, menuItem))
                    .Bind(CheckEntityBelongsToMenu)
                    .Bind(entityData => GetRow(
                        dataService,
                        entityData.Entity, input.DataStructureEntityId,
                        Guid.Empty, input.Id));
            }
            if(input.DataStructureEntityId == Guid.Empty)
            {
                return sessionObjects.UIService.GetRow(
                   sessionFormIdentifier: input.SessionFormIdentifier,
                   entity: input.Entity,
                   dataStructureEntity: null, 
                   rowId: input.Id); 
            }
            return FindEntity(input.DataStructureEntityId)
                .Bind(dataStructureEntity =>
                    sessionObjects.UIService.GetRow(
                        input.SessionFormIdentifier, input.Entity,
                        dataStructureEntity, input.Id));
        }
        private static Hashtable DictionaryToHashtable(IDictionary<string, object> source)
        {
            var result = new Hashtable(source.Count);
            foreach(var(key, value) in source)
            {
                result.Add(key, value);
            }
            return result;
        }
        private static bool AreColumnNamesValid(
            LookupListInput input, DataTable dataTable)
        {
            var actualColumnNames = dataTable.Columns
                .Cast<DataColumn>()
                .Select(x => x.ColumnName)
                .ToArray();
            return input.ColumnNames
                .All(colName => actualColumnNames.Contains(colName));
        }

        private Result<DataStructureQuery, IActionResult>
            WorkQueueGetRowsGetAggregationQuery(GetGroupsAggregations input,
                WorkQueueSessionStore sessionStore)
        {
            var query = new DataStructureQuery
            {
                Entity = workQueueEntity,
                CustomFilters = new CustomFilters
                {
                    Filters = input.Filter,
                    FilterLookups = input.FilterLookups ?? new Dictionary<string, Guid>()
                },
                ColumnsInfo = new ColumnsInfo(
                    columns: new List<ColumnData>(), 
                    renderSqlForDetachedFields: true),
                ForceDatabaseCalculation = true,
                AggregatedColumns = input.AggregatedColumns,
                MethodId = sessionStore.WQClass.WorkQueueStructureUserListMethodId,
                SortSetId = sessionStore.WQClass.WorkQueueStructureSortSetId,
                DataSourceId = sessionStore.WQClass.WorkQueueStructureId
            };
            var parameters = sessionObjects.UIService.GetParameters(
                sessionStore.Id);
            foreach(var key in parameters.Keys)
            {
                query.Parameters.Add(
                    new QueryParameter(key.ToString(),
                        parameters[key]));
            }
            query.Parameters.Add(new QueryParameter(
                "WorkQueueEntry_parWorkQueueId", sessionStore.Request.ObjectId));
            return query;
        }
        
        private Result<DataStructureQuery, IActionResult> GetRowsGetAggregationQuery(
            GetGroupsAggregations input, EntityData entityData)
        {
            var query = new DataStructureQuery
            {
                Entity = entityData.Entity.Name,
                CustomFilters = new CustomFilters
                {
                    Filters = input.Filter,
                    FilterLookups = input.FilterLookups ?? new Dictionary<string, Guid>()
                },
                ColumnsInfo = new ColumnsInfo(
                    columns: new List<ColumnData>(), 
                    renderSqlForDetachedFields: true),
                ForceDatabaseCalculation = true,
                AggregatedColumns = input.AggregatedColumns
            };
            return AddMethodAndSource(
                input.SessionFormIdentifier, input.MasterRowId, entityData, query);
        }

        private Result<QueryData, IActionResult> GetFilterListValuesQuery(
            GetFilterListValuesInput input, EntityData entityData)
        {
            var column = entityData.Entity.Column(input.Property);
            if (column == null)
            {
                return Result.Failure<QueryData, IActionResult>(
                    BadRequest($"Cannot get values for \"{input.Property}\" because the column does not exist."));
            }
            var field = column.Field;
            ColumnData columnData;
            string entity;
            if((field is DetachedField detachedField)
                && (detachedField.ArrayRelation != null))
            {
                columnData = new ColumnData(
                    name: detachedField.ArrayValueField.Name,
                    isVirtual: (detachedField.ArrayValueField is DetachedField),
                    defaultValue: (detachedField.ArrayValueField 
                        as DetachedField)
                    ?.DefaultValue?.Value,
                    hasRelation: (detachedField.ArrayValueField 
                        as DetachedField) ?.ArrayRelation != null);
                entity = detachedField.ArrayRelation.AssociatedEntity.Name;
            }
            else
            {
                columnData = new ColumnData(
                    name: input.Property,
                    isVirtual: (field is DetachedField),
                    defaultValue: (field as DetachedField)
                    ?.DefaultValue?.Value,
                    hasRelation: (field as DetachedField)
                    ?.ArrayRelation != null);
                entity = entityData.Entity.Name;
            }
            var columns = new List<ColumnData> {columnData};
            var query = new DataStructureQuery
            {
                Entity = entity,
                CustomFilters = new CustomFilters
                {
                    Filters = input.Filter,
                    FilterLookups = input.FilterLookups ?? new Dictionary<string, Guid>()
                },
                CustomOrderings = new CustomOrderings(new List<Ordering>()),
                ColumnsInfo = new ColumnsInfo(
                    columns: columns, 
                    renderSqlForDetachedFields: true),
                Distinct = true,
                ForceDatabaseCalculation = true,
                AggregatedColumns = new List<Aggregation>()
            };

            return new QueryData
            {
                SessionFormIdentifier = input.SessionFormIdentifier,
                DataStructureQuery = query,
                EntityData = entityData
            };
        }

        private class QueryData
        {
            public Guid SessionFormIdentifier { get; set; }
            public DataStructureQuery DataStructureQuery { get; set; }
            public EntityData EntityData { get; set; }
        }

        private Result<DataStructureQuery, IActionResult> GetRowsGetGroupQuery(
            GetGroupsInput input, EntityData entityData)
        {
            var customOrdering = GetOrderings(input.OrderingList);

            DataStructureColumn column = entityData.Entity.Column(input.GroupBy);
            if (column == null)
            {
                return Result.Failure<DataStructureQuery, IActionResult>(BadRequest($"Cannot group by \"{input.GroupBy}\" because the column does not exist."));
            }

            var field = column.Field;
            var columnData = new ColumnData(
                name: input.GroupBy,
                isVirtual: (field is DetachedField),
                defaultValue: (field as DetachedField)
                ?.DefaultValue?.Value,
                hasRelation: (field as DetachedField)
                ?.ArrayRelation != null);

            List<ColumnData> columns = new List<ColumnData>
                {columnData, ColumnData.GroupByCountColumn};

            if(input.GroupByLookupId != Guid.Empty)
            {
                columns.Add(ColumnData.GroupByCaptionColumn);
            }

            var query = new DataStructureQuery
            {
                Entity = entityData.Entity.Name,
                CustomFilters = new CustomFilters
                {
                    Filters = input.Filter,
                    FilterLookups = input.FilterLookups ?? new Dictionary<string, Guid>()
                },
                CustomOrderings = customOrdering,
                RowLimit = input.RowLimit,
                ColumnsInfo = new ColumnsInfo(
                    columns: columns, 
                    renderSqlForDetachedFields: true),
                ForceDatabaseCalculation = true,
                CustomGrouping= new Grouping(
                    input.GroupBy, input.GroupByLookupId, input.GroupingUnit),
                AggregatedColumns = input.AggregatedColumns 
            };
            return AddMethodAndSource(
                input.SessionFormIdentifier, input.MasterRowId, entityData, query);
        }
        private static void FillRow(
            RowData rowData, Dictionary<string, string> newValues)
        {
            foreach(var(key, value) in newValues)
            {
                var dataType = rowData.Row.Table.Columns[key].DataType;
                rowData.Row[key] = DatasetTools.ConvertValue(value, dataType);
            }
        }
        private RowData MakeEmptyRow(DataStructureEntity entity)
        {
            var dataSet = dataService.GetEmptyDataSet(
                entity.RootEntity.ParentItemId, CultureInfo.InvariantCulture);
            var table = dataSet.Tables[entity.Name];
            var row = table.NewRow();
            return new RowData { Entity = entity, Row = row };
        }
        private static RowData FillRow(NewRowInput input, RowData rowData)
        {
            DatasetTools.ApplyPrimaryKey(rowData.Row);
            DatasetTools.UpdateOrigamSystemColumns(
                rowData.Row, true, SecurityManager.CurrentUserProfile().Id);
            FillRow(rowData, input.NewValues);
            rowData.Row.Table.Rows.Add(rowData.Row);
            return rowData;
        }
        private static RowData PrepareNewRow(RowData rowData)
        {
            DatasetTools.ApplyPrimaryKey(rowData.Row);
            DatasetTools.UpdateOrigamSystemColumns(
                rowData.Row, true, SecurityManager.CurrentUserProfile().Id);
            rowData.Row.Table.NewRow();
            return rowData;
        }
        private IActionResult SubmitDelete(RowData rowData)
        {
            try
            {
                DataService.Instance.StoreData(
                    dataStructureId: rowData.Entity.RootEntity.ParentItemId,
                    data: rowData.Row.Table.DataSet,
                    loadActualValuesAfterUpdate: false,
                    transactionId: null);
            }
            catch(DBConcurrencyException ex)
            {
                if(string.IsNullOrEmpty(ex.Message) 
                    && (ex.InnerException != null))
                {
                    return Conflict(ex.InnerException.Message);
                }
                return Conflict(ex.Message);
            }
            return Ok(SessionStore.GetDeleteInfo(
                                requestingGrid: null,
                                tableName: rowData.Row.Table.TableName,
                                objectId: null));
        }
        private IActionResult ThrowAwayReturnData(IActionResult arg)
        {
            return Ok();
        }
        private IActionResult RowDataToRecordTooltip(RowData rowData)
        {
            var requestCultureFeature = Request.HttpContext.Features
                .Get<IRequestCultureFeature>();
            var cultureInfo = requestCultureFeature.RequestCulture.Culture;
            return Ok(ServerCoreUIService.DataRowToRecordTooltip(
                rowData.Row, 
                cultureInfo,
                localizer));
        }
        private IActionResult GetAuditLog(Guid entityId, object id)
        {
            var auditLog = AuditLogDA.RetrieveLogTransformed(
                entityId, id);
            if(log != null)
            {
                return Ok(DataTools.DatatableToHashtable(
                    auditLog.Tables[0], false));
            }
            return Ok();
        }
        
        private void AddConfigData(PortalResult result)
        {
            result.LogoUrl = string.IsNullOrWhiteSpace(customAssetsConfig.Html5ClientLogoUrl)
                ? "./img/logo-left.png"
                : customAssetsConfig.Html5ClientLogoUrl;
            result.ChatRefreshInterval = string.IsNullOrEmpty(chatConfig.PathToChatApp)
                ? 0
                : chatConfig.ChatRefreshInterval;
            result.CustomAssetsRoute = customAssetsConfig.RouteToCustomAssetsFolder;
            result.ShowToolTipsForMemoFieldsOnly = htmlClientConfig.ShowToolTipsForMemoFieldsOnly;
            result.RowStatesDebouncingDelayMilliseconds = htmlClientConfig.RowStatesDebouncingDelayMilliseconds;
            result.FilteringConfig = clientFilteringConfig;
        }
    }
}
