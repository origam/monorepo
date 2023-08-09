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

import xmlJs from "xml-js";
import axios, { AxiosInstance } from "axios";

import _ from "lodash";
import {
  IApi,
  IEntityExportField,
  ILazyLoadedEntityInput,
  IUIGridFilterCoreConfiguration,
  IUpdateData
} from "./types/IApi";
import { IAggregationInfo } from "./types/IAggregationInfo";
import { IOrdering } from "./types/IOrderingConfiguration";
import { TypeSymbol } from "dic/Container";
import { IAboutInfo } from "./types/IAboutInfo";
import { T } from "utils/translation";
import fileDownload from "js-file-download";
import { ITableConfiguration } from "model/entities/TablePanelView/types/IConfigurationManager";
import { PubSub } from "utils/events";
import { layoutToString } from "model/entities/TablePanelView/layout";


export enum IAuditLogColumnIndices {
  Id = 0,
  DateTime = 1,
  UserName = 2,
  FieldName = 3,
  OldValue = 4,
  NewValue = 5,
  ActionType = 6,
}

export class OrigamAPI implements IApi {
  constructor(errorHandler: (error: any) => void) {
    this.urlPrefix = "/internalApi";
    this.chatroomsUrlPrefix = "/chatrooms";
    this.axiosInstance = this.createAxiosInstance();
    this.errorHandler = errorHandler;
  }

  private createAxiosInstance() {
    const axiosInstance = axios.create({
      baseURL: this.urlPrefix,
      headers: this.httpAuthHeader,
    });

    axiosInstance.interceptors.response.use(
      (response) => {
        this.onApiResponse.trigger({});
        return response;
      },
      async (error) => {
        if (error.response?.data?.constructor?.name === 'Blob') {
          error.response.data = await error.response.data.text();
        }
        if (!axios.isCancel(error)) {
          this.errorHandler(error);
        }
        throw error;
      }
    );
    return axiosInstance;
  }

  onApiResponse = new PubSub<{}>();

  errorHandler: (error: any) => void;
  axiosInstance: AxiosInstance;
  urlPrefix: string;
  chatroomsUrlPrefix: string;
  accessToken = "";

  setAccessToken(token: string) {
    this.accessToken = token;
    this.axiosInstance = this.createAxiosInstance();
  }

  resetAccessToken(): void {
    this.accessToken = "";
  }

  get httpAuthHeader() {
    return {
      Authorization: `Bearer ${this.accessToken}`,
    };
  }

  createCanceller(): () => void {
    const tokenSource = axios.CancelToken.source();
    const token = tokenSource.token;
    const canceller = () => {
      tokenSource.cancel();
    };
    canceller.token = token;
    return canceller as any;
  }

  _getCancelToken(canceller: any) {
    return canceller.any;
  }

  async login(credentials: { UserName: string; Password: string }) {
    return (await this.axiosInstance.post("/User/Login", credentials)).data;
  }

  async logout() {
    return await this.axiosInstance.post("/User/Logout", {});
  }

  async getScreen(id: string) {
    return xmlJs.xml2js(
      (
        await this.axiosInstance.get("/UI/GetUI", {
          params: {id},
          headers: this.httpAuthHeader,
        })
      ).data,
      {addParent: true, alwaysChildren: true}
    );
  }

  async initUI(data: {
    Type: string;
    FormSessionId: string | undefined;
    IsNewSession: boolean;
    RegisterSession: boolean;
    DataRequested: boolean;
    ObjectId: string;
    Caption: string;
    Parameters: { [key: string]: any };
    IsSingleRecordEdit?: boolean;
    RequestCurrentRecordId: boolean;
  }) {
    const result = (await this.axiosInstance.post("/UIService/InitUI", data)).data;
    return {
      ...result,
      formDefinition: xmlJs.xml2js(result.formDefinition, {
        addParent: true,
        alwaysChildren: true,
      }),
    };
  }

  async destroyUI(data: { FormSessionId: string }) {
    return (await this.axiosInstance.post(`/UIService/DestroyUI`, data)).data;
  }

  async getEntities(query: {
    MenuId: string;
    DataStructureEntityId: string;
    Ordering: Array<[string, string]>;
    ColumnNames: string[];
    Filter: string;
    RowLimit?: number;
    MasterRowId?: string;
  }) {
    const response = (await this.axiosInstance.post("/Data/GetRows", query)).data;

    if (_.isString(response.data)) {
      return [];
    } else {
      return response.data;
    }
  }

  async getLookupLabels(query: {
    LookupId: string;
    MenuId: string | undefined;
    LabelIds: string[];
  }) {
    return (await this.axiosInstance.post("/UIService/GetLookupLabels", query)).data;
  }

  async getLookupLabelsEx(
    query: {
      LookupId: string;
      MenuId: string | undefined;
      LabelIds: string[];
    }[]
  ) {
    if (query.length === 1 && query[0].LabelIds.length === 0) {
      return {LookupId: {}}
    }
    return (await this.axiosInstance.post("/UIService/GetLookupLabelsEx", query)).data;
  }

  async newEntity(data: { DataStructureEntityId: string; MenuId: string }) {
    return (await this.axiosInstance.post("/Data/NewEmptyRow", data)).data;
  }

  async putEntity(data: {
    DataStructureEntityId: string;
    RowId: string;
    NewValues: { [key: string]: any };
    MenuId: string;
  }) {
    return (await this.axiosInstance.put("/Data/Row", data)).data;
  }

  async postEntity(data: {
    DataStructureEntityId: string;
    NewValues: { [key: string]: any };
    MenuId: string;
  }) {
    return (await this.axiosInstance.post("/Data/Row", data)).data;
  }

  async deleteEntity(data: {
    DataStructureEntityId: string;
    RowIdToDelete: string;
    MenuId: string;
  }) {
    return (
      await this.axiosInstance.request({
        url: `${this.urlPrefix}/Data/Row`,
        method: "DELETE",
        data,
        headers: this.httpAuthHeader,
      })
    ).data;
  }

  async createEntity() {
    // TODO
  }

  async createSession(data: {
    MenuId: string;
    Parameters: { [key: string]: any };
    InitializeStructure: boolean;
  }) {
    return (await this.axiosInstance.post("/Session/CreateSession", data)).data;
  }

  async deleteSession() {
  }

  async saveSession(sessionFormIdentifier: string) {
    return (await this.axiosInstance.get(`/UIService/SaveData/${sessionFormIdentifier}`)).data;
  }

  async saveSessionQuery(sessionFormIdentifier: string) {
    return (await this.axiosInstance.get(`/UIService/SaveDataQuery/${sessionFormIdentifier}`)).data;
  }

  async refreshSession(sessionFormIdentifier: string) {
    return (await this.axiosInstance.get(`/UIService/RefreshData/${sessionFormIdentifier}`)).data;
  }

  async revertChanges(data: { sessionFormIdentifier: string }) {
    await this.axiosInstance.post("/UIService/RevertChanges", data);
  }

  async sessionChangeMasterRecord(data: {
    SessionFormIdentifier: string;
    Entity: string;
    RowId: string;
  }) {
    return (await this.axiosInstance.post("/Session/ChangeMasterRecord", data)).data;
  }

  async sessionDeleteEntity(data: {
    SessionFormIdentifier: string;
    Entity: string;
    RowId: string;
  }) {
    return (await this.axiosInstance.post("/Session/DeleteRow", data)).data;
  }

  async sessionCreateEntity(data: {
    SessionFormIdentifier: string;
    Entity: string;
    Values: { [key: string]: any };
    Parameters: { [key: string]: any };
    RequestingGridId: string;
  }) {
    return (await this.axiosInstance.post(`/Session/CreateRow`, data)).data;
  }

  async sessionGetEntity(data: {
    sessionFormIdentifier: string;
    childEntity: string;
    parentRecordId: string;
    rootRecordId: string;
  }) {
    return (
      await this.axiosInstance.get("/Session/Rows", {
        params: data,
        headers: this.httpAuthHeader,
      })
    ).data;
  }

  // TODO: Remove this method.
  async sessionUpdateEntity(data: {
    SessionFormIdentifier: string;
    Entity: string;
    Id: string;
    Property: string;
    NewValue: any;
  }): Promise<any> {
    return (await this.axiosInstance.post("/Session/UpdateRow", data)).data;
  }

  async getLookupList(data: {
    DataStructureEntityId?: string;
    FormSessionIdentifier?: string;
    Entity?: string;
    ColumnNames: string[];
    Property: string;
    Id: string;
    LookupId: string;
    Parameters?: { [key: string]: any };
    ShowUniqueValues: boolean;
    SearchText: string;
    PageSize: number;
    PageNumber: number;
    MenuId: string;
  }): Promise<any> {
    return (await this.axiosInstance.post("/UIService/GetLookupList", data)).data;
  }

  async getLookupCacheDependencies(data: { LookupIds: string[] }): Promise<any> {
    return (await this.axiosInstance.post("/UIService/GetLookupCacheDependencies", data)).data;
  }

  async getNotificationBoxContent(): Promise<any> {
    return (await this.axiosInstance.get("/UIService/GetNotificationBoxContent")).data;
  }

  async defaultLocalizationCookie(): Promise<any> {
    return (await this.axiosInstance.get("/UIService/DefaultLocalizationCookie")).data;
  }

  async initPortal(): Promise<any> {
    const data = (await this.axiosInstance.get("/UIService/InitPortal")).data;

    return {
      ...data,
      menu: xmlJs.xml2js(data.menu, {addParent: true, alwaysChildren: true}),
    };
  }

  async setMasterRecord(
    data: {
      SessionFormIdentifier: string;
      Entity: string;
      RowId: string;
    },
    canceller?: any
  ) {
    return (
      (
        await this.axiosInstance.post("/UIService/MasterRecord", data, {
          headers: this.httpAuthHeader,
          cancelToken: canceller && canceller.token,
        })
      )?.data ?? []
    );
  }

  async restoreData(data: { SessionFormIdentifier: string; ObjectId: string }) {
    return (await this.axiosInstance.post("/UIService/RestoreData", data)).data;
  }
  async updateObject(data: {
    SessionFormIdentifier: string;
    Entity: string;
    UpdateData: IUpdateData[];
  }): Promise<any> {
    return (await this.axiosInstance.post("/UIService/UpdateObject", data)).data;
  }

  async createObject(data: {
    SessionFormIdentifier: string;
    Entity: string;
    Values: { [key: string]: any };
    Parameters: { [key: string]: any };
    RequestingGridId: string;
  }): Promise<any> {
    return (await this.axiosInstance.post("/UIService/CreateObject", data)).data;
  }

  async copyObject(data: {
    Entity: string;
    SessionFormIdentifier: string;
    ForcedValues: {};
    RequestingGridId: string;
    OriginalId: string;
    Entities: string[];
  }): Promise<any> {
    return (await this.axiosInstance.post("/UIService/CopyObject", data)).data;
  }

  async deleteObject(data: {
    SessionFormIdentifier: string;
    Entity: string;
    Id: string;
  }): Promise<any> {
    return (await this.axiosInstance.post("/UIService/DeleteObject", data)).data;
  }

  async deleteObjectInOrderedList(data: {
    SessionFormIdentifier: string;
    Entity: string;
    Id: string;
    OrderProperty: string;
    UpdatedOrderValues: {};
  }): Promise<any> {
    return (await this.axiosInstance.post("/UIService/DeleteObjectInOrderedList", data)).data;
  }

  async executeActionQuery(data: {
    SessionFormIdentifier: string;
    Entity: string;
    ActionType: string;
    ActionId: string;
    ParameterMappings: { [key: string]: any };
    SelectedIds: string[];
    InputParameters: { [key: string]: any };
  }): Promise<any> {
    return (await this.axiosInstance.post("/UIService/ExecuteActionQuery", data)).data;
  }

  async executeAction(data: {
    SessionFormIdentifier: string;
    Entity: string;
    ActionType: string;
    ActionId: string;
    ParameterMappings: { [key: string]: any };
    SelectedIds: string[];
    InputParameters: { [key: string]: any };
    RequestingGrid: string;
  }): Promise<any> {
    return (await this.axiosInstance.post(`/UIService/ExecuteAction`, data)).data;
  }

  async getReportInfo(data: {
    ReportId: string
  }): Promise<any> {
    return (await this.axiosInstance.get(`/Report/GetReportInfo?reportRequestId=` + data.ReportId)).data;
  }

  async getFilterListValues(data: {
    MenuId: string;
    DataStructureEntityId: string;
    Property: string;
    SessionFormIdentifier: string | undefined;
    Filter: string;
    FilterLookups?: { [key: string]: string };
  }): Promise<any[]> {
    return (await this.axiosInstance.post(`/UIService/GetFilterListValues`, data)).data;
  }

  async getGroups(data: {
    MenuId: string;
    DataStructureEntityId: string;
    Filter: string | undefined;
    FilterLookups?: { [key: string]: string };
    Ordering: IOrdering[];
    RowLimit: number;
    GroupBy: string;
    GroupingUnit: string | undefined;
    MasterRowId: string | undefined;
    GroupByLookupId: string | undefined;
    SessionFormIdentifier: string | undefined;
    AggregatedColumns: IAggregationInfo[] | undefined;
  }): Promise<any[]> {
    const resultData = (await this.axiosInstance.post(`/UIService/GetGroups`, data)).data;
    if (resultData.length > 50_000) {
      throw new Error(T("There are too many groups, choose different grouping options please.", "too_many_groups"));
    }
    return resultData;
  }

  async getAggregations(data: {
    MenuId: string;
    DataStructureEntityId: string;
    Filter: string | undefined;
    FilterLookups?: { [key: string]: string };
    AggregatedColumns: IAggregationInfo[];
    SessionFormIdentifier: string | undefined;
    MasterRowId: string | undefined;
  }): Promise<any[]> {
    return (await this.axiosInstance.post(`/UIService/GetAggregations`, data)).data;
  }

  async getRows(data: {
    MenuId: string;
    SessionFormIdentifier: string;
    DataStructureEntityId: string;
    Ordering: IOrdering[];
    RowLimit: number;
    RowOffset: number;
    Parameters: { [key: string]: string };
    ColumnNames: string[];
    MasterRowId: string | undefined;
    Filter: string;
    FilterLookups?: { [key: string]: string };
  }): Promise<any> {
    return (await this.axiosInstance.post(`/UIService/GetRows`, data)).data;
  }

  async getRow(data: {
    SessionFormIdentifier: string;
    Entity: string;
    RowId: string;
  }): Promise<any> {
    return (await this.axiosInstance.post(`/UIService/GetRow`, data)).data;
  }

  async getData(data: {
    SessionFormIdentifier: string;
    ChildEntity: string;
    ParentRecordId: string;
    RootRecordId: string;
  }): Promise<any> {
    return (await this.axiosInstance.post(`/UIService/GetData`, data)).data;
  }

  getReportFromMenu(data: { menuId: string }): Promise<any>;

  async getReportFromMenu(data: { menuId: string }): Promise<string> {
    return (await this.axiosInstance.get(`/UIService/ReportFromMenu/${data.menuId}`)).data;
  }

  async getRowStates(data: {
    SessionFormIdentifier: string;
    Entity: string;
    Ids: string[];
  }): Promise<any> {
    let states = (await this.axiosInstance.post(`/UIService/RowStates`, data)).data;
    for (const state of states) {
      state.id = state.id.toString();
    }
    return states;
  }

  async saveFavorites(data: {
    ConfigXml: string;
  }): Promise<any> {
    return (await this.axiosInstance.post(`/UIService/SaveFavorites`, data)).data;
  }

  async getWorkQueueList(): Promise<any> {
    return (await this.axiosInstance.get(`/UIService/WorkQueueList`)).data;
  }

  async getChatroomList(): Promise<any> {
    return (await axios.get(`${this.chatroomsUrlPrefix}/Chat`, {headers: this.httpAuthHeader}))
      .data;
  }

  async saveObjectConfiguration(data: {
    sessionFormIdentifier: string;
    instanceId: string;
    tableConfigurations: ITableConfiguration[];
    customConfigurations?: {[nodeName: string] : string};
    defaultView: string;
  }): Promise<any> {
    let customConfigurationXml = "";
    if(data.customConfigurations){
      const customConfigurations = Object.entries(data.customConfigurations)
        .filter(entry => entry[0] && entry[1])
        .map(entry => {
          const encodedConfig = window.btoa(decodeURIComponent(encodeURIComponent(entry[1])))
          return `<${entry[0]}Configuration>\n${encodedConfig}\n</${entry[0]}Configuration>`
        });
      if(customConfigurations.length > 0) {
        customConfigurationXml = `<CustomConfigurations>\n${customConfigurations.join("\n")}\n</CustomConfigurations>`
      }
    }

    const tableConfigurationsXml = data.tableConfigurations.map(tableConfig => {
      return "<TableConfiguration" +
        ` name="${tableConfig.name ?? ""}"` +
        ` fixedColumnCount="${tableConfig.fixedColumnCount}"` +
        ` isActive="${tableConfig.isActive}"` +
        ` id="${tableConfig.id}"` +
        ` layout="${layoutToString(tableConfig.layout)}"` +
        ">" +
        tableConfig.columnConfigurations
          .map(columnConfig =>
            "<ColumnConfiguration" +
            ` propertyId="${columnConfig.propertyId}"` +
            ` width="${columnConfig.width}"` +
            (columnConfig.timeGroupingUnit !== undefined ? ` groupingUnit="${columnConfig.timeGroupingUnit}"` : "") +
            (columnConfig.groupingIndex > 0 ? ` groupingIndex="${columnConfig.groupingIndex}"` : "") +
            ` isVisible="${columnConfig.isVisible}"` +
            (columnConfig.aggregationType ? ` aggregationType="${columnConfig.aggregationType}"` : "") +
            "/>")
          .join("\n")
        + "</TableConfiguration>"
    })
    await this.axiosInstance.post(`/UIService/SaveObjectConfig`, {
      ObjectInstanceId: data.instanceId,
      SectionNameAndData: {
        tableConfigurations: tableConfigurationsXml.join("\n"),
        customConfigurations: customConfigurationXml,
        defaultView: `<view id="${data.defaultView}" />`,
      }
    });
  }

  async resetScreenColumnConfiguration(data: {
    instanceId: string;
  }): Promise<any> {
    await this.axiosInstance.post(`/UIService/ResetScreenColumnConfiguration`, {
      ObjectInstanceId: data.instanceId,
    });
  }

  async saveSplitPanelConfiguration(data: { InstanceId: string; Position: number }): Promise<any> {
    return (await this.axiosInstance.post(`/UIService/SaveSplitPanelConfig`, data)).data;
  }

  async workflowAbort(data: { sessionFormIdentifier: string }): Promise<any> {
    const result = (
      await this.axiosInstance.get(`/UIService/WorkflowAbort/${data.sessionFormIdentifier}`)
    ).data;

    return {
      ...result,
      formDefinition: xmlJs.xml2js(result.formDefinition, {
        addParent: true,
        alwaysChildren: true,
      }),
    };
  }

  async workflowRepeat(data: { sessionFormIdentifier: string }): Promise<any> {
    const result = (
      await this.axiosInstance.get(`/UIService/WorkflowRepeat/${data.sessionFormIdentifier}`)
    ).data;

    return {
      ...result,
      formDefinition: xmlJs.xml2js(result.formDefinition, {
        addParent: true,
        alwaysChildren: true,
      }),
    };
  }

  async workflowNext(data: {
    sessionFormIdentifier: string;
    CachedFormIds: string[];
  }): Promise<any> {
    const result = (
      await this.axiosInstance.post(`/UIService/WorkflowNext`, {
        SessionFormIdentifier: data.sessionFormIdentifier,
        CachedFormIds: data.CachedFormIds,
      })
    ).data;

    return {
      ...result,
      formDefinition: xmlJs.xml2js(result.formDefinition, {
        addParent: true,
        alwaysChildren: true,
      }),
    };
  }

  async workflowNextQuery(data: { sessionFormIdentifier: string }): Promise<any> {
    return (
      await this.axiosInstance.get(`/UIService/WorkflowNextQuery/${data.sessionFormIdentifier}`)
    ).data;
  }

  async getRecordInfo(data: { MenuId: string; DataStructureEntityId: string; RowId: string }) {
    return (await this.axiosInstance.post(`/UIService/GetRecordTooltip`, data)).data.tooltip;
  }

  async getRecordAudit(data: { MenuId: string; DataStructureEntityId: string; RowId: string }) {
    return (await this.axiosInstance.post(`/UIService/GetAudit`, data)).data.data.map(
      (row: any[]) => ({
        id: row[IAuditLogColumnIndices.Id],
        dateTime: row[IAuditLogColumnIndices.DateTime],
        userName: row[IAuditLogColumnIndices.UserName],
        fieldName: row[IAuditLogColumnIndices.FieldName],
        oldValue: row[IAuditLogColumnIndices.OldValue],
        newValue: row[IAuditLogColumnIndices.NewValue],
        actionType: row[IAuditLogColumnIndices.ActionType],
      })
    );
  }

  async getReport(data: { reportUrl: string }): Promise<any> {
    return (await this.axiosInstance.get(data.reportUrl)).data;
  }

  async getDownloadToken(data: {
    SessionFormIdentifier: string;
    MenuId: string;
    DataStructureEntityId: string;
    RowId: string;
    Property: string;
    FileName: string;
    Entity: string;
    parameters: any;
    isPreview: boolean;
  }): Promise<any> {
    return (
      await this.axiosInstance.post(`/Blob/DownloadToken`, {
        SessionFormIdentifier: data.SessionFormIdentifier,
        MenuId: data.MenuId,
        DataStructureEntityId: data.DataStructureEntityId,
        Entity: data.Entity,
        RowId: data.RowId,
        Property: data.Property,
        IsPreview: data.isPreview,
        Parameters: data.parameters,
      })
    ).data;
  }

  getBlobUrl(data: { downloadToken: string }) {
    return `${this.urlPrefix}/Blob/${data.downloadToken}`;
  }

  async getBlob(data: { downloadToken: string }) {
    window.open(this.getBlobUrl(data));
  }

  async getUploadToken(data: {
    SessionFormIdentifier: string;
    MenuId: string;
    DataStructureEntityId: string;
    Entity: string;
    RowId: string;
    Property: string;
    FileName: string;
    DateCreated: string;
    DateLastModified: string;
    parameters: any;
  }): Promise<any> {
    return (
      await this.axiosInstance.post(`/Blob/UploadToken`, {
        SessionFormIdentifier: data.SessionFormIdentifier,
        MenuId: data.MenuId,
        DataStructureEntityId: data.DataStructureEntityId,
        Entity: data.Entity,
        RowId: data.RowId,
        Property: data.Property,
        IsPreview: false,
        Parameters: data.parameters,
        SubmitImmediately: "true",
      })
    ).data;
  }

  async putBlob(
    data: { uploadToken: string; fileName: string; file: any },
    onUploadProgress?: (event: any) => void
  ): Promise<any> {
    return (
      await this.axiosInstance.post(`/Blob/${data.uploadToken}/${data.fileName}`, data.file, {
        headers: {...this.httpAuthHeader, "content-type": "application/octet-stream"},
        onUploadProgress,
      })
    ).data;
  }

  async pendingChanges(data: { sessionFormIdentifier: string }): Promise<any[]> {
    return (await this.axiosInstance.get(`/UIService/PendingChanges/${data.sessionFormIdentifier}`))
      .data;
  }

  async changes(data: {
    SessionFormIdentifier: string;
    Entity: string;
    RowId: string;
  }): Promise<any[]> {
    return (
      await this.axiosInstance.post(`/UIService/Changes`, {
        SessionFormIdentifier: data.SessionFormIdentifier,
        Entity: data.Entity,
        RowId: data.RowId,
      })
    ).data;
  }

  async saveFilter(data: {
    DataStructureEntityId: string;
    PanelId: string;
    Filter: IUIGridFilterCoreConfiguration;
    IsDefault: boolean;
  }): Promise<string> {
    return (await this.axiosInstance.post(`/UIService/SaveFilter`, data)).data;
  }

  async deleteFilter(data: { filterId: string }): Promise<any[]> {
    return (await this.axiosInstance.post(`/UIService/DeleteFilter`, data)).data;
  }

  async resetDefaultFilter(data: {
    SessionFormIdentifier: string;
    PanelInstanceId: string;
  }): Promise<any[]> {
    return (await this.axiosInstance.post(`/UIService/ResetDefaultFilter`, data)).data;
  }

  async setDefaultFilter(data: {
    SessionFormIdentifier: string;
    PanelInstanceId: string;
    DataStructureEntityId: string;
    PanelId: string;
    Filter: IUIGridFilterCoreConfiguration;
    IsDefault: boolean;
  }): Promise<any[]> {
    return (await this.axiosInstance.post(`/UIService/SetDefaultFilter`, data)).data;
  }

  async search(searchTerm: string) {
    return (await this.axiosInstance.get(`/Search/${encodeURIComponent(searchTerm)}`)).data;
  }

  async getAboutInfo() {
    return (await this.axiosInstance.get("/About")).data as IAboutInfo;
  }

  async getMenuId(data: { LookupId: string; ReferenceId: string }): Promise<string> {
    return (await this.axiosInstance.post(`/UIService/GetMenuId`, data)).data;
  }

  async getExcelFile(data: {
    Entity: string;
    Fields: IEntityExportField[];
    SessionFormIdentifier: string;
    RowIds: any[];
    LazyLoadedEntityInput: ILazyLoadedEntityInput | undefined;
  }): Promise<any> {
    const response = await this.axiosInstance({
      url: `/ExcelExport/GetFile`,
      method: 'POST',
      data: data,
      responseType: 'blob',
    })

    const fieNameRegex = /filename=([^\s;]*)/g

    let fileName = "export.xls";
    if (response.headers["content-disposition"]) {
      const headerFileName = fieNameRegex.exec(response.headers["content-disposition"])?.[1]
      if (headerFileName) {
        fileName = headerFileName;
      }
    }

    fileDownload(response.data, fileName);
  }

  async getMenuIdByReference(data: { Category: string; ReferenceId: any }): Promise<string> {
    return (await this.axiosInstance.post(`/DeepLink/GetMenuId`, data)).data;
  }
}

export const IOrigamAPI = TypeSymbol<OrigamAPI>("IOrigamAPI");
