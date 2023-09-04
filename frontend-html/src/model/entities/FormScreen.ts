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

import { IDataView } from "./types/IDataView";
import { IDataSource } from "./types/IDataSource";
import { IComponentBinding } from "./types/IComponentBinding";
import { IFormScreenLifecycle02 } from "./types/IFormScreenLifecycle";
import { action, computed, observable } from "mobx";
import { IAction } from "./types/IAction";
import { isLazyLoading } from "model/selectors/isLazyLoading";
import {
  IFormScreen,
  IFormScreenData,
  IFormScreenEnvelope,
  IFormScreenEnvelopeData,
  IScreenNotification,
} from "./types/IFormScreen";
import { IPanelConfiguration } from "./types/IPanelConfiguration";
import { CriticalSection } from "utils/sync";
import { getRowStates } from "model/selectors/RowState/getRowStates";
import { ScreenPictureCache } from "./ScreenPictureCache";
import { DataViewCache } from "./DataViewCache";
import { getFormScreen } from "model/selectors/FormScreen/getFormScreen";
import { getSessionId } from "model/selectors/getSessionId";
import { getEntity } from "model/selectors/DataView/getEntity";
import { getSelectedRowId } from "model/selectors/TablePanelView/getSelectedRowId";
import { ICRUDResult, IResponseOperation, processCRUDResult } from "model/actions/DataLoading/processCRUDResult";
import { getApi } from "model/selectors/getApi";
import { ScreenFocusManager } from "model/entities/ScreenFocusManager";

export class FormScreen implements IFormScreen {

  $type_IFormScreen: 1 = 1;
  notifications: IScreenNotification[] = [];

  constructor(data: IFormScreenData) {
    Object.assign(this, data);
    this.formScreenLifecycle.parent = this;
    this.dataViews.forEach((o) => (o.parent = this));
    this.dataSources.forEach((o) => (o.parent = this));
    this.componentBindings.forEach((o) => (o.parent = this));
    this.focusManager.parent = this;
  }

  focusManager: ScreenFocusManager = null as any;

  parent?: any;

  dataUpdateCRS = new CriticalSection();
  pictureCache = new ScreenPictureCache();
  dataViewCache = new DataViewCache(this);

  @observable isDirty: boolean = false;
  uiRootType = "";
  dynamicTitleSource: string | undefined;
  sessionId: string = "";
  @observable title: string = "";
  suppressSave: boolean = false;
  suppressRefresh: boolean = false;
  menuId: string = "";
  openingOrder: number = 0;
  showInfoPanel: boolean = false;
  showWorkflowCancelButton: boolean = false;
  showWorkflowNextButton: boolean = false;
  autoRefreshInterval: number = 0;
  refreshOnFocus: boolean = false;
  cacheOnClient: boolean = false;
  autoSaveOnListRecordChange: boolean = false;
  requestSaveAfterUpdate: boolean = false;
  screenUI: any;
  @observable
  panelConfigurations: Map<string, IPanelConfiguration> = new Map();
  isLoading: false = false;
  formScreenLifecycle: IFormScreenLifecycle02 = null as any;
  autoWorkflowNext: boolean = null as any;
  workflowTaskId: string | null = null;

  dataViews: IDataView[] = [];
  dataSources: IDataSource[] = [];
  componentBindings: IComponentBinding[] = [];

  setPanelSize(id: string, size: number) {
    if (!this.panelConfigurations.has(id)) {
      this.panelConfigurations.set(
        id,
        {
          position: undefined,
          defaultOrdering: undefined
        }
      )
    }
    this.panelConfigurations.get(id)!.position = size;
  }

  getData(childEntity: string, modelInstanceId: string, parentRecordId: string, rootRecordId: string) {
    this.dataSources.filter(dataSource => dataSource.entity === childEntity)
      .forEach(dataSource => getRowStates(dataSource).clearAll());
    return this.dataViewCache.getData({
      childEntity: childEntity,
      modelInstanceId: modelInstanceId,
      parentRecordId: parentRecordId,
      rootRecordId: rootRecordId
    });
  }

  clearDataCache() {
    this.dataViewCache.clear();
  }


  get dynamicTitle() {
    if (!this.dynamicTitleSource) {
      return undefined;
    }
    const splitSource = this.dynamicTitleSource.split(".");
    const dataSourceName = splitSource[0];
    const columnName = splitSource[1];

    const dataView = this.dataViews.find((view) => view.entity === dataSourceName);
    if (!dataView) return undefined;
    const dataSource = this.dataSources.find((view) => view.entity === dataSourceName);
    if (!dataSource) return undefined;
    const dataSourceField = dataSource!.getFieldByName(columnName);
    const dataTable = dataView!.dataTable;
    const firstRow = dataTable.rows[0];
    return firstRow
      ? dataTable.getCellValueByDataSourceField(firstRow, dataSourceField!)
      : undefined;
  }

  @computed get isLazyLoading() {
    return isLazyLoading(this);
  }

  @computed get rootDataViews(): IDataView[] {
    return this.dataViews.filter((dv) => dv.isBindingRoot);
  }

  @computed get nonRootDataViews(): IDataView[] {
    return this.dataViews.filter((dv) => !dv.isBindingRoot);
  }

  @action.bound setTitle(title: string) {
    this.title = title;
  }

  getBindingsByChildId(childId: string) {
    return this.componentBindings.filter((b) => b.childId === childId);
  }

  getBindingsByParentId(parentId: string) {
    return this.componentBindings.filter((b) => b.parentId === parentId);
  }

  getDataViewByModelInstanceId(modelInstanceId: string): IDataView | undefined {
    return this.dataViews.find((dv) => dv.modelInstanceId === modelInstanceId);
  }

  getDataViewsByEntity(entity: string): IDataView[] {
    return this.dataViews.filter((dv) => dv.entity === entity);
  }

  getDataSourceByEntity(entity: string): IDataSource | undefined {
    return this.dataSources.find((ds) => ds.entity === entity);
  }

  @computed get toolbarActions() {
    const result: Array<{ section: string; actions: IAction[] }> = [];
    for (let dv of this.dataViews) {
      if (dv.toolbarActions.length > 0) {
        result.push({
          section: dv.name,
          actions: dv.toolbarActions,
        });
      }
    }
    return result;
  }

  @computed get dialogActions() {
    const result: IAction[] = [];
    for (let dv of this.dataViews) {
      result.push(...dv.dialogActions);
    }
    return result;
  }

  getPanelPosition(id: string): number | undefined {
    const conf = this.panelConfigurations.get(id.toLowerCase());
    return conf ? conf.position : undefined;
  }

  @action.bound
  setDirty(state: boolean): void {
    if (this.suppressSave && state === true) {
      return;
    }
    this.isDirty = state;
  }

  getFirstFormPropertyId() {
    for (let dv of this.dataViews) {
      for (let prop of dv.properties) {
        if (prop.isFormField) return prop.id;
      }
    }
  }

  /* eslint-disable no-console */
  printMasterDetailTree() {
    const strrep = (cnt: number, str: string) => {
      let result = "";
      for (let i = 0; i < cnt; i++) result = result + str;
      return result;
    };

    const recursive = (dataView: IDataView, level: number) => {
      console.log(
        `${strrep(level, "  ")}${dataView?.name} (${dataView?.entity} - ${dataView?.modelId})`
      );
      if (!dataView) {
        return;
      }
      for (let chb of dataView.childBindings) {
        recursive(chb.childDataView, level + 1);
      }
    };
    console.log("View bindings");
    console.log("=============");
    const roots = Array.from(this.dataViews.values()).filter((dv) => dv.isBindingRoot);
    for (let dv of roots) {
      recursive(dv, 0);
    }
    console.log("=============");
    console.log("End of View bindings");
    console.log("");
  }

  /* eslint-enable no-console */
}

export class FormScreenEnvelope implements IFormScreenEnvelope {
  $type_IFormScreenEnvelope: 1 = 1;

  constructor(data: IFormScreenEnvelopeData) {
    Object.assign(this, data);
    this.formScreenLifecycle.parent = this;
  }

  @observable formScreen?: IFormScreen | undefined;
  formScreenLifecycle: IFormScreenLifecycle02 = null as any;

  @computed get isLoading() {
    return !this.formScreen;
  }

  @action.bound
  setFormScreen(formScreen?: IFormScreen | undefined): void {
    if (formScreen) {
      formScreen.parent = this;
    }
    this.formScreen = formScreen;
  }

  *start(args: {initUIResult: any, preloadIsDirty?: boolean}): Generator {
    yield*this.formScreenLifecycle.start(args.initUIResult);
    if (this.formScreen) {
      this.formScreen.setDirty(!!args.preloadIsDirty);
      if(args.preloadIsDirty && isLazyLoading(this.formScreen)){
       yield*this.loadDirtyRow(this.formScreen);
      }
    }
  }

  *loadDirtyRow(formScreen: IFormScreen){
    for (let rootDataView of formScreen.rootDataViews) {
      const api  = getApi(rootDataView)
      const isDirty = getFormScreen(rootDataView).isDirty
      if(isDirty){
        const dirtyRowResult = (yield api.getRow({
          SessionFormIdentifier: getSessionId(rootDataView),
          Entity: getEntity(rootDataView),
          RowId: getSelectedRowId(rootDataView)!
        })) as ICRUDResult;

        const selectedRowExists = rootDataView.selectedRowId &&
          rootDataView.dataTable.getRowById(rootDataView.selectedRowId);
        dirtyRowResult.operation = selectedRowExists
          ? IResponseOperation.Update
          : IResponseOperation.Create;

        yield*processCRUDResult(rootDataView, dirtyRowResult) as any;
      }
    }
  }

  parent?: any;
}


