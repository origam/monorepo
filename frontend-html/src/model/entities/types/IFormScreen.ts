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

import { IDataView } from "./IDataView";
import { IDataSource } from "./IDataSource";
import { IComponentBinding } from "./IComponentBinding";
import { IFormScreenLifecycle02 } from "./IFormScreenLifecycle";
import { IAction } from "./IAction";
import { IRefreshOnReturnType } from "../WorkbenchLifecycle/WorkbenchLifecycle";
import { IPanelConfiguration } from "./IPanelConfiguration";
import { CriticalSection } from "utils/sync";
import { ScreenPictureCache } from "../ScreenPictureCache";
import { DataViewCache } from "../DataViewCache";
import { ScreenFocusManager } from "model/entities/ScreenFocusManager";

/*
export interface ILoadedFormScreenData {
  title: string;
  menuId: string;
  openingOrder: number;
  showInfoPanel: boolean;
  autoRefreshInterval: number;
  cacheOnClient: boolean;
  autoSaveOnListRecordChange: boolean;
  requestSaveAfterUpdate: boolean;
  dataViews: IDataView[];
  dataSources: IDataSource[];
  componentBindings: IComponentBinding[];
  screenUI: any;
  formScreenLifecycle: IFormScreenLifecycle;
  sessionId: string;
}

export interface ILoadedFormScreen extends ILoadedFormScreenData {
  $type_ILoadedFormScreen: 1;

  isDirty: boolean;

  isLoading: false;
  rootDataViews: IDataView[];
  dontRequestData: boolean;

  getBindingsByChildId(childId: string): IComponentBinding[];
  getBindingsByParentId(parentId: string): IComponentBinding[];
  getDataViewByModelInstanceId(modelInstanceId: string): IDataView | undefined;
  getDataViewsByEntity(entity: string): IDataView[];
  getDataSourceByEntity(entity: string): IDataSource | undefined;

  toolbarActions: Array<{ section: string; actions: IAction[] }>;
  dialogActions: IAction[];

  setDirty(state: boolean): void;

  printMasterDetailTree(): void;

  parent?: any;
}

export interface ILoadingFormScreenData {
  formScreenLifecycle: IFormScreenLifecycle;
}

export interface ILoadingFormScreen extends ILoadingFormScreenData {
  $type_ILoadingFormScreen: 1;

  isLoading: true;
  parent?: any;

  start(): void;
}

export type IFormScreen = ILoadingFormScreen | ILoadedFormScreen;

export const isILoadingFormScreen = (o: any): o is ILoadingFormScreen =>
  o.$type_ILoadingFormScreen;
export const isILoadedFormScreen = (o: any): o is ILoadedFormScreen =>
  o.$type_ILoadedFormScreen; */

export interface IFormScreenEnvelopeData {
  formScreenLifecycle: IFormScreenLifecycle02;
  preloadedSessionId?: string;
  refreshOnReturnType?: IRefreshOnReturnType;
}

export interface IFormScreenEnvelope extends IFormScreenEnvelopeData {
  $type_IFormScreenEnvelope: 1;

  isLoading: boolean;
  formScreen?: IFormScreen;

  setFormScreen(formScreen?: IFormScreen): void;

  start(args: {initUIResult: any, preloadIsDirty?: boolean, isWorkQueueScreen?: boolean}): Generator;

  parent?: any;
}

export interface IFormScreenData {
  title: string;
  menuId: string;
  dynamicTitleSource: string | undefined;
  openingOrder: number;
  autoWorkflowNext: boolean;
  showInfoPanel: boolean;
  showWorkflowCancelButton: boolean;
  showWorkflowNextButton: boolean;
  autoRefreshInterval: number;
  refreshOnFocus: boolean;
  cacheOnClient: boolean;
  suppressSave: boolean;
  suppressRefresh: boolean;
  autoSaveOnListRecordChange: boolean;
  requestSaveAfterUpdate: boolean;
  dataViews: IDataView[];
  dataSources: IDataSource[];
  componentBindings: IComponentBinding[];
  screenUI: any;
  panelConfigurations: Map<string, IPanelConfiguration>;
  formScreenLifecycle: IFormScreenLifecycle02;
  sessionId: string;
  workflowTaskId: string | null;
  uiRootType: string;
  focusManager: ScreenFocusManager;
}

export interface IFormScreen extends IFormScreenData {
  $type_IFormScreen: 1;

  isDirty: boolean;

  isLoading: false;
  rootDataViews: IDataView[];
  nonRootDataViews: IDataView[];
  isLazyLoading: boolean;
  toolbarActions: Array<{ section: string; actions: IAction[] }>;
  dialogActions: IAction[];
  dynamicTitle: string | undefined;

  dataUpdateCRS: CriticalSection;
  notifications: IScreenNotification[]
  pictureCache: ScreenPictureCache;
  dataViewCache: DataViewCache;

  clearDataCache(): void;

  getPanelPosition(id: string): number | undefined;

  getData(childEntity: string, modelInstanceId: string, parentRecordId: string, rootRecordId: string): Promise<any>;

  getBindingsByChildId(childId: string): IComponentBinding[];

  getBindingsByParentId(parentId: string): IComponentBinding[];

  getDataViewByModelInstanceId(modelInstanceId: string): IDataView | undefined;

  getDataViewsByEntity(entity: string): IDataView[];

  getDataSourceByEntity(entity: string): IDataSource | undefined;

  getFirstFormPropertyId(): string | undefined;

  setPanelSize(id: string, size: number): void;

  setDirty(state: boolean): void;

  setTitle(title: string): void;

  printMasterDetailTree(): void;

  parent?: any;
}

export interface IScreenNotification {
  icon: string,
  text: string
}

export const isIFormScreenEnvelope = (o: any): o is IFormScreenEnvelope =>
  o.$type_IFormScreenEnvelope;

export const isIFormScreen = (o: any): o is IFormScreen => o.$type_IFormScreen;
