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

import { IAggregationInfo } from "./IAggregationInfo";
import { IOrdering } from "./IOrderingConfiguration";
import { IServerSearchResult } from "model/entities/types/ISearchResult";
import { IAboutInfo } from "./IAboutInfo";
import { ITableConfiguration } from "model/entities/TablePanelView/types/IConfigurationManager";
import { EventHandler } from "utils/events";

export interface IApi {
  getAboutInfo(): Promise<IAboutInfo>;

  accessToken: string;

  setAccessToken(token: string | undefined): void;

  resetAccessToken(): void;

  httpAuthHeader: { Authorization: string };

  createCanceller(): () => void;

  onApiResponse: EventHandler<{}>;

  login(credentials: { UserName: string; Password: string }): Promise<string>;

  logout(): Promise<any>;

  getScreen(id: string): Promise<any>;

  getEntities(query: {
    MenuId: string;
    DataStructureEntityId: string;
    Ordering: Array<[string, string]>;
    ColumnNames: string[];
    Filter: string;
    RowLimit?: number;
    MasterRowId?: string;
  }): Promise<any>;

  getLookupLabels(query: {
    LookupId: string;
    MenuId: string | undefined;
    LabelIds: string[];
  }): Promise<{ [key: string]: string }>;

  getLookupCacheDependencies(data: { LookupIds: string[] }): Promise<any>;

  getLookupLabelsEx(
    query: {
      LookupId: string;
      MenuId: string | undefined;
      LabelIds: string[];
    }[]
  ): Promise<{ [key: string]: { [key: string]: string } }>;

  newEntity(data: { DataStructureEntityId: string; MenuId: string }): Promise<any>;

  putEntity(data: {
    DataStructureEntityId: string;
    RowId: string;
    NewValues: { [key: string]: any };
    MenuId: string;
  }): Promise<any>;

  postEntity(data: {
    DataStructureEntityId: string;
    NewValues: { [key: string]: any };
    MenuId: string;
  }): Promise<any>;

  deleteEntity(data: {
    DataStructureEntityId: string;
    RowIdToDelete: string;
    MenuId: string;
  }): Promise<any>;

  createSession(data: {
    MenuId: string;
    Parameters: { [key: string]: any };
    InitializeStructure: boolean;
  }): Promise<any>;

  saveSession(sessionFormIdentifier: string): Promise<any>;

  saveSessionQuery(sessionFormIdentifier: string): Promise<any>;

  refreshSession(sessionFormIdentifier: string): Promise<any>;

  revertChanges(data: { sessionFormIdentifier: string }): Promise<any>;

  sessionChangeMasterRecord(data: {
    SessionFormIdentifier: string;
    Entity: string;
    RowId: string;
  }): Promise<any>;

  sessionGetEntity(data: {
    sessionFormIdentifier: string;
    childEntity: string;
    parentRecordId: string;
    rootRecordId: string;
  }): Promise<any>;

  sessionUpdateEntity(data: {
    SessionFormIdentifier: string;
    Entity: string;
    Id: string;
    Property: string;
    NewValue: any;
  }): Promise<any>;

  sessionCreateEntity(data: {
    SessionFormIdentifier: string;
    Entity: string;
    Values: { [key: string]: any };
    Parameters: { [key: string]: any };
    RequestingGridId: string;
  }): Promise<any>;

  sessionDeleteEntity(data: {
    SessionFormIdentifier: string;
    Entity: string;
    RowId: string;
  }): Promise<any>;

  getLookupList(data: {
    SessionFormIdentifier?: string;
    Entity?: string;
    DataStructureEntityId?: string;
    ColumnNames: string[];
    Property: string;
    Id: string;
    MenuId: string;
    LookupId: string;
    Parameters?: { [key: string]: any };
    ShowUniqueValues: boolean;
    SearchText: string;
    PageSize: number;
    PageNumber: number;
  }): Promise<any>;

  getNotificationBoxContent(): Promise<any>;

  defaultLocalizationCookie(): Promise<any>;

  initPortal(): Promise<any>;

  initUI(data: {
    Type: string;
    FormSessionId: string | undefined;
    IsNewSession: boolean;
    RegisterSession: boolean;
    DataRequested: boolean;
    ObjectId: string;
    Caption: string;
    Parameters: { [key: string]: any } | undefined;
    IsSingleRecordEdit?: boolean;
    RequestCurrentRecordId: boolean;
  }): Promise<any>;

  destroyUI(data: { FormSessionId: string }): Promise<any>;

  setMasterRecord(
    data: {
      SessionFormIdentifier: string;
      Entity: string;
      RowId: string;
    },
    canceller?: any
  ): Promise<any>;

  restoreData(data: { SessionFormIdentifier: string; ObjectId: string }): Promise<void>;

  updateObject(data: {
    SessionFormIdentifier: string;
    Entity: string;
    UpdateData: IUpdateData[];
  }): Promise<any>;

  createObject(data: {
    SessionFormIdentifier: string;
    Entity: string;
    Values: { [key: string]: any };
    Parameters: { [key: string]: any };
    RequestingGridId: string;
  }): Promise<any>;

  copyObject(data: {
    Entity: string;
    SessionFormIdentifier: string;
    ForcedValues: {};
    RequestingGridId: string;
    OriginalId: string;
    Entities: string[];
  }): Promise<any>;

  deleteObject(data: { SessionFormIdentifier: string; Entity: string; Id: string }): Promise<any>;

  deleteObjectInOrderedList(data: {
    SessionFormIdentifier: string;
    Entity: string;
    Id: string;
    OrderProperty: string;
    UpdatedOrderValues: {};
  }): Promise<any>;

  executeActionQuery(data: {
    SessionFormIdentifier: string;
    Entity: string;
    ActionType: string;
    ActionId: string;
    ParameterMappings: { [key: string]: any };
    SelectedIds: string[];
    InputParameters: { [key: string]: any };
  }): Promise<any>;

  executeAction(data: {
    SessionFormIdentifier: string;
    Entity: string;
    ActionType: string;
    ActionId: string;
    ParameterMappings: { [key: string]: any };
    SelectedIds: string[];
    InputParameters: { [key: string]: any };
    RequestingGrid: string;
  }): Promise<any>;

  getReportInfo(data: {
    ReportId: string
  }): Promise<any>

  getRows(data: {
    MenuId: string;
    SessionFormIdentifier: string;
    DataStructureEntityId: string;
    Filter: string;
    Ordering: IOrdering[];
    RowLimit: number;
    RowOffset: number;
    Parameters: { [key: string]: string };
    ColumnNames: string[];
    MasterRowId: string | undefined;
    FilterLookups?: { [key: string]: string };
  }): Promise<any>;

  getRow(data: {
    SessionFormIdentifier: string;
    Entity: string;
    RowId: string;
  }): Promise<any>

  getFilterListValues(data: {
    MenuId: string;
    DataStructureEntityId: string;
    Property: string;
    SessionFormIdentifier: string | undefined;
    Filter: string;
    FilterLookups?: { [key: string]: string };
  }): Promise<any[]>;

  getGroups(data: {
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
  }): Promise<any[]>;

  getAggregations(data: {
    MenuId: string;
    DataStructureEntityId: string;
    Filter: string | undefined;
    FilterLookups?: { [key: string]: string };
    AggregatedColumns: IAggregationInfo[];
    SessionFormIdentifier: string | undefined;
    MasterRowId: string | undefined;
  }): Promise<any[]>;

  getData(data: {
    SessionFormIdentifier: string;
    ChildEntity: string;
    ParentRecordId: string;
    RootRecordId: string;
  }): Promise<any>;

  getReportFromMenu(data: { menuId: string }): Promise<string>;

  getRowStates(data: {
    SessionFormIdentifier: string;
    Entity: string;
    Ids: string[];
  }): Promise<any>;

  saveFavorites(data: {
    ConfigXml: string;
  }): Promise<any>;

  getWorkQueueList(): Promise<any>;

  getChatroomList(): Promise<any>;

  saveObjectConfiguration(data: {
    sessionFormIdentifier: string;
    instanceId: string;
    tableConfigurations: ITableConfiguration[];
    customConfigurations?: {[nodeName: string] : string};
    defaultView: string;
  }): Promise<any>;

  resetScreenColumnConfiguration(data: {
    instanceId: string;
  }): Promise<any>;

  saveSplitPanelConfiguration(data: { InstanceId: string; Position: number }): Promise<any>;

  workflowAbort(data: { sessionFormIdentifier: string }): Promise<any>;

  workflowRepeat(data: { sessionFormIdentifier: string }): Promise<any>;

  workflowNext(data: { sessionFormIdentifier: string; CachedFormIds: string[] }): Promise<any>;

  workflowNextQuery(data: { sessionFormIdentifier: string }): Promise<any>;

  getRecordInfo(data: {
    MenuId: string;
    DataStructureEntityId: string;
    RowId: string;
  }): Promise<any>;

  getRecordAudit(data: {
    MenuId: string;
    DataStructureEntityId: string;
    RowId: string;
  }): Promise<Array<{
    id: string;
    dateTime: string;
    userName: string;
    fieldName: string;
    oldValue: string | null;
    newValue: string | null;
    actionType: number;
  }>>;

  getReport(data: { reportUrl: string }): Promise<any>;

  getDownloadToken(data: {
    SessionFormIdentifier: string;
    MenuId: string;
    DataStructureEntityId: string;
    Entity: string;
    RowId: string;
    Property: string;
    FileName: string;
    parameters: any;
    isPreview: boolean;
  }): Promise<any>;

  getBlob(data: { downloadToken: string }): Promise<any>;
  getBlobUrl(data: { downloadToken: string }): string;

  getUploadToken(data: {
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
  }): Promise<any>;

  putBlob(
    data: { uploadToken: string; fileName: string; file: any },
    onUploadProgress?: (event: any) => void
  ): Promise<any>;

  pendingChanges(data: { sessionFormIdentifier: string }): Promise<any[]>;

  changes(data: { SessionFormIdentifier: string; Entity: string; RowId: string }): Promise<any[]>;

  search(searchTerm: string): Promise<IServerSearchResult[]>;

  setDefaultFilter(data: {
    SessionFormIdentifier: string;
    PanelInstanceId: string;
    DataStructureEntityId: string;
    PanelId: string;
    Filter: IUIGridFilterCoreConfiguration;
    IsDefault: boolean;
  }): Promise<any>;

  resetDefaultFilter(data: {
    SessionFormIdentifier: string;
    PanelInstanceId: string;
  }): Promise<any>;

  saveFilter(data: {
    DataStructureEntityId: string;
    PanelId: string;
    Filter: IUIGridFilterCoreConfiguration;
    IsDefault: boolean;
  }): Promise<string>;

  deleteFilter(data: { filterId: string }): Promise<any>;

  getMenuId(data: { LookupId: string; ReferenceId: string }): Promise<string>;

  getMenuIdByReference(data: { Category: string; ReferenceId: any }): Promise<string>;

  getExcelFile(data: {
    Entity: string;
    Fields: IEntityExportField[];
    SessionFormIdentifier: string;
    RowIds: any[];
    LazyLoadedEntityInput: ILazyLoadedEntityInput | undefined;
  }): Promise<any>;
}

export interface ILazyLoadedEntityInput {
  MenuId: string;
  DataStructureEntityId: string;
  Filter: string;
  Ordering: IOrdering[];
  RowLimit: number;
  RowOffset: number;
  ColumnNames: string[];
  FilterLookups?: { [key: string]: string };
  SessionFormIdentifier: string;
}

export interface IEntityExportField {
  Caption: string;
  FieldName: string;
  LookupId: string | undefined;
  Format: string;
  PolymorphRules: IPolymorphRules | undefined;
}

export interface IPolymorphRules {
  ControlField: string;
  Rules: any;
}

export interface IUIGridFilterCoreConfiguration {
  id: string | undefined;
  name: string;
  isGlobal: boolean;
  details: IUIGridFilterFieldConfiguration[];
}

export interface IUIGridFilterFieldConfiguration {
  property: string;
  value1: any;
  value2: any;
  operator: number;
}

export interface IUpdateData {
  RowId: string;
  Values: { [key: string]: any };
}
