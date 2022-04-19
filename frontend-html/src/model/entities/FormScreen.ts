import { IDataView } from "./types/IDataView";
import { IDataSource } from "./types/IDataSource";
import { IComponentBinding } from "./types/IComponentBinding";
import { IFormScreenLifecycle02 } from "./types/IFormScreenLifecycle";
import { action, computed, observable } from "mobx";
import { IAction } from "./types/IAction";
import {isLazyLoading} from "model/selectors/isLazyLoading";
import {
  IFormScreen,
  IFormScreenData,
  IFormScreenEnvelope,
  IFormScreenEnvelopeData,
} from "./types/IFormScreen";
import { IPanelConfiguration } from "./types/IPanelConfiguration";
import { CriticalSection } from "utils/sync";
import {getSessionId} from "model/selectors/getSessionId";
import { getEntity } from "model/selectors/DataView/getEntity";
import { getApi } from "model/selectors/getApi";
import {getRowStates} from "model/selectors/RowState/getRowStates";
import { ScreenPictureCache } from "./ScreenPictureCache";
import { getFormScreen } from "model/selectors/FormScreen/getFormScreen";

import { getSelectedRowId } from "model/selectors/TablePanelView/getSelectedRowId";
import { ICRUDResult, IResponseOperation, processCRUDResult } from "model/actions/DataLoading/processCRUDResult";

export class FormScreen implements IFormScreen {
  
  $type_IFormScreen: 1 = 1;

  constructor(data: IFormScreenData) {
    Object.assign(this, data);
    this.formScreenLifecycle.parent = this;
    this.dataViews.forEach((o) => (o.parent = this));
    this.dataSources.forEach((o) => (o.parent = this));
    this.componentBindings.forEach((o) => (o.parent = this));
  }

  parent?: any;

  dataUpdateCRS = new CriticalSection();
  pictureCache = new ScreenPictureCache();
  getDataCache = new GetDataCache(this);

  @observable isDirty: boolean = false;
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

  dataViews: IDataView[] = [];
  dataSources: IDataSource[] = [];
  componentBindings: IComponentBinding[] = [];

  setPanelSize(id: string, size: number) {
    if(!this.panelConfigurations.has(id)){
      this.panelConfigurations.set(
          id,
          {
            position:undefined,
            defaultOrdering: undefined}
          )
    }
    this.panelConfigurations.get(id)!.position = size;
  }

  getData(childEntity: string, parentRecordId: string, rootRecordId: string) {
    this.dataSources.filter(dataSource => dataSource.entity === childEntity)
    .forEach(dataSource => getRowStates(dataSource).clearAll());
    return this.getDataCache.getData({
      childEntity: childEntity,
      parentRecordId: parentRecordId,
      rootRecordId: rootRecordId,
      useCachedValue: !this.isDirty
    });
  }

  clearDataCache(){
    this.getDataCache.clear();
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
    console.log("");
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

  *start(initUIResult: any, preloadIsDirty?: boolean): Generator {
    yield* this.formScreenLifecycle.start(initUIResult);
    if (this.formScreen) {
      this.formScreen.setDirty(!!preloadIsDirty);
      if(preloadIsDirty && isLazyLoading(this.formScreen)){
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


class GetDataCache {
  constructor(private ctx: any) {}

  dataMap = new Map<string, Promise<any>>();

  public async getData(args:{childEntity: string, parentRecordId: string, rootRecordId: string, useCachedValue: boolean}) {
    const cacheKey = this.makeCacheKey(args.childEntity, args.parentRecordId, args.rootRecordId);
    if(!args.useCachedValue){
      if(this.dataMap.has(cacheKey)){
        this.dataMap.delete(cacheKey);
      }
      return await this.callGetData(args.childEntity, args.parentRecordId, args.rootRecordId);
    }
    if(!this.dataMap.has(cacheKey)) {
      const dataPromise = this.callGetData(args.childEntity, args.parentRecordId, args.rootRecordId);
      this.dataMap.set(cacheKey, dataPromise);
    }
    return this.dataMap.get(cacheKey);
  }

  public clear(){
    this.dataMap.clear();
  }

  private callGetData(childEntity: string, parentRecordId: string, rootRecordId: string) {
    const api = getApi(this.ctx);
    const dataPromise = api.getData({
      SessionFormIdentifier: getSessionId(this.ctx),
      ChildEntity: childEntity,
      ParentRecordId: parentRecordId,
      RootRecordId: rootRecordId,
    });
    return dataPromise;
  }

  private makeCacheKey(childEntity: string, parentRecordId: string, rootRecordId: string) {
    return childEntity + parentRecordId + rootRecordId;
  }
}
