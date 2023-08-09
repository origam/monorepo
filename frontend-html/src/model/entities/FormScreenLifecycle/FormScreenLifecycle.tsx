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

import { QuestionSaveData } from "gui/Components/Dialogs/QuestionSaveData";
import { action, autorun, comparer, flow, observable, reaction, when } from "mobx";
import { new_ProcessActionResult } from "model/actions/Actions/processActionResult";
import { closeForm } from "model/actions/closeForm";
import { processCRUDResult } from "model/actions/DataLoading/processCRUDResult";
import { handleError } from "model/actions/handleError";
import { refreshWorkQueues } from "model/actions/WorkQueues/refreshWorkQueues";
import { IAction } from "model/entities/types/IAction";
import { getBindingParametersFromParent } from "model/selectors/DataView/getBindingParametersFromParent";
import { getColumnNamesToLoad } from "model/selectors/DataView/getColumnNamesToLoad";
import { getDataStructureEntityId } from "model/selectors/DataView/getDataStructureEntityId";
import { getDataViewByGridId } from "model/selectors/DataView/getDataViewByGridId";
import { getDataViewsByEntity } from "model/selectors/DataView/getDataViewsByEntity";
import { getAutorefreshPeriod as getAutoRefreshPeriod } from "model/selectors/FormScreen/getAutorefreshPeriod";
import { getDataViewList } from "model/selectors/FormScreen/getDataViewList";
import { getIsFormScreenDirty } from "model/selectors/FormScreen/getisFormScreenDirty";
import { getIsSuppressSave } from "model/selectors/FormScreen/getIsSuppressSave";
import { showDialog } from "model/selectors/getDialogStack";
import { getIsActiveScreen } from "model/selectors/getIsActiveScreen";
import { map2obj } from "utils/objects";
import { interpretScreenXml } from "xmlInterpreters/screenXml";
import { getFormScreen } from "../../selectors/FormScreen/getFormScreen";
import { getApi } from "../../selectors/getApi";
import { getMenuItemId } from "../../selectors/getMenuItemId";
import { getOpenedScreen } from "../../selectors/getOpenedScreen";
import { getSessionId } from "../../selectors/getSessionId";
import { IFormScreenLifecycle02 } from "../types/IFormScreenLifecycle";
import { IDataView } from "../types/IDataView";
import { IAggregationInfo } from "../types/IAggregationInfo";
import { SCROLL_ROW_CHUNK } from "gui/Workbench/ScreenArea/TableView/InfiniteScrollLoader";
import { IQueryInfo, processActionQueryInfo } from "model/actions/Actions/processActionQueryInfo";
import { assignIIds, find } from "xmlInterpreters/xmlUtils";
import { IOrderByDirection, IOrdering } from "../types/IOrderingConfiguration";
import { getOrderingConfiguration } from "../../selectors/DataView/getOrderingConfiguration";
import { getFilterConfiguration } from "../../selectors/DataView/getFilterConfiguration";
import { getUserFilters } from "../../selectors/DataView/getUserFilters";
import { getUserOrdering } from "../../selectors/DataView/getUserOrdering";
import { FlowBusyMonitor } from "utils/flow";
import { IScreenEvents } from "modules/Screen/FormScreen/ScreenEvents";
import { scopeFor } from "dic/Container";
import { getUserFilterLookups } from "../../selectors/DataView/getUserFilterLookups";
import _, { isArray } from "lodash";
import { YesNoQuestion } from "@origam/components";
import { getProperties } from "model/selectors/DataView/getProperties";
import { getWorkbench } from "model/selectors/getWorkbench";
import { shouldProceedToChangeRow } from "model/actions-ui/DataView/TableView/shouldProceedToChangeRow";
import { getGroupingConfiguration } from "model/selectors/TablePanelView/getGroupingConfiguration";
import { startEditingFirstCell } from "model/actions/DataView/startEditingFirstCell";
import { getFormFocusManager } from "model/selectors/DataView/getFormFocusManager";
import { getDataSourceFieldByName } from "model/selectors/DataSources/getDataSourceFieldByName";
import { isLazyLoading } from "model/selectors/isLazyLoading";
import { getAllBindingChildren } from "model/selectors/DataView/getAllBindingChildren";
import { getEntity } from "model/selectors/DataView/getEntity";
import { isInfiniteScrollingActive } from "model/selectors/isInfiniteScrollingActive";
import { AggregationType } from "../types/AggregationType";
import { calcAggregations, parseAggregations } from "../Aggregatioins";
import { UpdateRequestAggregator } from "./UpdateRequestAggregator";
import { IGroupingSettings } from "../types/IGroupingConfiguration";
import { groupingUnitToString } from "../types/GroupingUnit";
import { getTablePanelView } from "../../selectors/TablePanelView/getTablePanelView";
import { getFormScreenLifecycle } from "../../selectors/FormScreen/getFormScreenLifecycle";
import { runGeneratorInFlowWithHandler, runInFlowWithHandler } from "utils/runInFlowWithHandler";
import { onFieldBlur } from "../../actions-ui/DataView/TableView/onFieldBlur";
import { getRowStates } from "../../selectors/RowState/getRowStates";
import { getIsAddButtonVisible } from "../../selectors/DataView/getIsAddButtonVisible";
import { pluginLibrary } from "plugins/tools/PluginLibrary";
import { isIScreenPlugin, isISectionPlugin } from "@origam/plugins";
import { refreshRowStates } from "model/actions/RowStates/refreshRowStates";
import { T } from "utils/translation";
import { askYesNoQuestion } from "gui/Components/Dialog/DialogUtils";
import { getDataView } from "model/selectors/DataView/getDataView";
import { getConfigurationManager } from "model/selectors/TablePanelView/getConfigurationManager";
import { isMobileLayoutActive } from "model/selectors/isMobileLayoutActive";
import { IMainMenuItemType } from "model/entities/types/IMainMenu";

enum IQuestionSaveDataAnswer {
  Cancel = 0,
  NoSave = 1,
  Save = 2,
}

enum IQuestionDeleteDataAnswer {
  No = 0,
  Yes = 1,
}

export const closingScreens = new WeakSet<any>();

export class FormScreenLifecycle02 implements IFormScreenLifecycle02 {
  $type_IFormScreenLifecycle: 1 = 1;

  parameters: { [key: string]: string } = {};
  focusedDataViewId: string | undefined;
  _updateRequestAggregator: UpdateRequestAggregator | undefined;

  get updateRequestAggregator() {
    if (!this._updateRequestAggregator) {
      this._updateRequestAggregator = new UpdateRequestAggregator(getApi(this));
    }
    return this._updateRequestAggregator;
  }

  @observable allDataViewsSteady = true;

  monitor: FlowBusyMonitor = new FlowBusyMonitor();

  get isWorkingDelayed() {
    return this.monitor.isWorkingDelayed;
  }

  initialSelectedRowId: string | undefined;

  get isWorking() {
    return this.monitor.isWorking;
  }

  disposers: (() => void)[] = [];

  registerDisposer(disposer: () => void) {
    this.disposers.push(disposer);
  }

  *onFlushData(): Generator<unknown, any, unknown> {
    yield*this.flushData();
  }

  *onCreateRow(entity: string, gridId: string): Generator<unknown, any, unknown> {
    yield*this.createRow(entity, gridId);
  }

  *onCopyRow(entity: any, gridId: string, rowId: string): any {
    yield*this.copyRow(entity, gridId, rowId);
  }

  *onDeleteRow(
    entity: string,
    rowId: string,
    dataView: IDataView
  ): Generator<unknown, any, unknown> {
    yield*this.onRequestDeleteRow(entity, rowId, dataView);
  }

  *onSaveSession(): Generator<unknown, any, unknown> {
    yield*this.flushData();
    yield*this.saveSession();
  }

  *onExecuteAction(
    gridId: string,
    entity: string,
    action: IAction,
    selectedItems: string[]
  ): Generator<any, any, any> {
    if (action.confirmationMessage && !(yield askYesNoQuestion(this, getOpenedScreen(this).tabTitle, action.confirmationMessage))) {
      return;
    }
    yield*this.executeAction(gridId, entity, action, selectedItems);
  }

  *onRequestDeleteRow(entity: string, rowId: string, dataView: IDataView): any {
    if ((yield this.questionDeleteData()) === IQuestionDeleteDataAnswer.Yes) {
      yield*this.deleteRow(entity, rowId, dataView);
    }
  }

  *onRequestScreenClose(isDueToError?: boolean): Generator<unknown, any, unknown> {
    const formScreen = getFormScreen(this);
    for (let dataView of formScreen.dataViews) {
      yield onFieldBlur(dataView)();
    }

    // Just wait if there is some data manipulation in progress.
    yield formScreen.dataUpdateCRS.runAsync(() => Promise.resolve());
    if (isDueToError || !getIsFormScreenDirty(this) || getIsSuppressSave(this)) {
      yield*this.closeForm();
      return;
    }
    switch (yield this.questionSaveData()) {
      case IQuestionSaveDataAnswer.Cancel:
        return;
      case IQuestionSaveDataAnswer.Save:
        const saveSuccessful = yield*this.saveSession();
        if (saveSuccessful) {
          yield*this.closeForm();
        }
        return;
      case IQuestionSaveDataAnswer.NoSave:
        yield*this.closeForm();
        return;
    }
  }

  *onRequestScreenReload(): Generator<unknown, any, unknown> {
    if (!getIsFormScreenDirty(this) || getIsSuppressSave(this)) {
      yield*this.refreshSession();
      return;
    }
    getFormScreen(this).dataViews.forEach((dataView) => dataView.onReload());
    switch (yield this.questionSaveData()) {
      case IQuestionSaveDataAnswer.Cancel:
        return;
      case IQuestionSaveDataAnswer.Save:
        yield*this.saveSession();
        yield*this.refreshSession();
        return;
      case IQuestionSaveDataAnswer.NoSave:
        if (!this.eagerLoading) {
          yield*this.revertChanges();
        }
        yield*this.refreshSession();
        return;
    }
  }

  *revertChanges(): Generator<unknown, any, unknown> {
    const api = getApi(this);
    yield api.revertChanges({sessionFormIdentifier: getSessionId(this)});
  }

  *onWorkflowNextClick(event: any): Generator {
    this.monitor.inFlow++;
    try {
      const api = getApi(this);
      const sessionId = getSessionId(this);
      const actionQueryInfo = (yield api.workflowNextQuery({
        sessionFormIdentifier: sessionId,
      })) as IQueryInfo[];
      const formScreen = getFormScreen(this);
      const processQueryInfoResult = yield*processActionQueryInfo(this)(
        actionQueryInfo,
        formScreen.title
      );
      if (!processQueryInfoResult.canContinue) return;
      let uiResult;
      try {
        yield*formScreen.dataUpdateCRS.enterGenerator();
        uiResult = yield api.workflowNext({
          sessionFormIdentifier: sessionId,
          CachedFormIds: [],
        });
      } finally {
        formScreen.dataUpdateCRS.leave();
      }
      this.killForm();
      yield*this.start(uiResult);
    } finally {
      this.monitor.inFlow--;
    }
  }

  *onWorkflowAbortClick(event: any): Generator {
    this.monitor.inFlow++;
    try {
      const api = getApi(this);
      let uiResult;
      const formScreen = getFormScreen(this);
      try {
        yield*formScreen.dataUpdateCRS.enterGenerator();
        uiResult = yield api.workflowAbort({sessionFormIdentifier: getSessionId(this)});
      } finally {
        formScreen.dataUpdateCRS.leave();
      }
      this.killForm();
      yield*this.start(uiResult);
    } finally {
      this.monitor.inFlow--;
    }
  }

  *onWorkflowRepeatClick(event: any): Generator {
    this.monitor.inFlow++;
    try {
      const api = getApi(this);
      const sessionId = getSessionId(this);
      let uiResult;
      const formScreen = getFormScreen(this);
      try {
        yield*formScreen.dataUpdateCRS.enterGenerator();
        uiResult = yield api.workflowRepeat({sessionFormIdentifier: sessionId});
      } finally {
        formScreen.dataUpdateCRS.leave();
      }
      this.killForm();
      yield*this.start(uiResult);
    } finally {
      this.monitor.inFlow--;
    }
  }

  *onWorkflowCloseClick(event: any): Generator {
    this.monitor.inFlow++;
    try {
      const formScreen = getFormScreen(this);
      try {
        yield*formScreen.dataUpdateCRS.enterGenerator();
      } finally {
        formScreen.dataUpdateCRS.leave();
      }
      yield*this.onRequestScreenClose();
    } finally {
      this.monitor.inFlow--;
    }
  }

  _autoRefreshTimerHandle: any;

  *startAutoRefreshIfNeeded() {
    const autoRefreshPeriod = getAutoRefreshPeriod(this);
    if (autoRefreshPeriod) {
      this.disposers.push(
        autorun(() => {
          if (
            !getIsSuppressSave(this) &&
            (getIsFormScreenDirty(this) || !getIsActiveScreen(this))
          ) {
            this.clearAutoRefreshInterval();
          } else {
            this._autoRefreshTimerHandle = setInterval(
              () => this.performAutoReload(),
              autoRefreshPeriod * 1000
            );
          }
        })
      );
    }
  }

  clearAutoRefreshInterval() {
    if (this._autoRefreshTimerHandle) {
      clearInterval(this._autoRefreshTimerHandle);
      this._autoRefreshTimerHandle = undefined;
    }
  }

  performAutoReload() {
    const self = this;
    flow(function*() {
      try {
        const formScreen = getFormScreen(self);
        formScreen.dataViews.forEach((dataView) => getRowStates(dataView).suppressWorkingStatus = true);
        yield*self.refreshSession();
      } catch (e) {
        yield*handleError(self)(e);
        throw e;
      } finally {
        const formScreen = getFormScreen(self);
        formScreen.dataViews.forEach((dataView) => getRowStates(dataView).suppressWorkingStatus = false);
      }
    })();
  }

  *start(initUIResult: any): Generator {
    let _steadyDebounceTimeout: any;
    this.disposers.push(
      reaction(
        () => getFormScreen(this).dataViews.every((dv) => !dv.isWorking) && !this.isWorkingDelayed,
        (allDataViewsSteady) => {
          if (allDataViewsSteady) {
            _steadyDebounceTimeout = setTimeout(() => {
              _steadyDebounceTimeout = undefined;
              this.allDataViewsSteady = true;
            }, 100);
          } else {
            this.allDataViewsSteady = false;
            if (_steadyDebounceTimeout) {
              clearTimeout(_steadyDebounceTimeout);
              _steadyDebounceTimeout = undefined;
            }
          }
        }
      ),
      () => {
        clearTimeout(_steadyDebounceTimeout);
        _steadyDebounceTimeout = undefined;
      }
    );
    this.disposers.push(
      reaction(
        () => getFormScreen(this).dataViews.every((dv) => !dv.isWorking) && !this.isWorking,
        (workFinished) => {
          const rootDataViews = getFormScreen(this).rootDataViews;
          if(rootDataViews.length !== 1){
            return;
          }
          const rootDataView = rootDataViews[0];
          const filtersDisplayed = getTablePanelView(rootDataView)!.filterConfiguration
            .isFilterControlsDisplayed
          if(workFinished && !filtersDisplayed && rootDataView.isTableViewActive()){
              rootDataView.formFocusManager.refocusLast();
          }
        }
      ),
    );
    try {
      const openedScreen = getOpenedScreen(this);
      if(!openedScreen){
        return;
      }
      this.initialSelectedRowId = initUIResult.currentRecordId;
      yield*this.applyInitUIResult({initUIResult});
      this.initializePlugins(initUIResult);
      if (!this.eagerLoading) {
        yield*this.clearTotalCounts();
        yield*this.loadData();
        yield*this.updateTotalRowCounts();
        const formScreen = getFormScreen(this);
        for (let rootDataView of formScreen.rootDataViews) {
          const orderingConfiguration = getOrderingConfiguration(rootDataView);
          const filterConfiguration = getFilterConfiguration(rootDataView);
          this.disposers.push(
            reaction(
              () => {
                const filters = filterConfiguration.activeFilters
                  .map((x) => [
                    x.propertyId,
                    x.setting.type,
                    isArray(x.setting.val1) ? [...x.setting.val1] : x.setting.val1,
                    x.setting.val2,
                  ])
                  .filter((item) => {
                    if (
                      item[1] === "in" ||
                      item[1] === "eq" ||
                      item[1] === "neq" ||
                      item[1] === "nin" ||
                      item[1] === "neq" ||
                      item[1] === "contains" ||
                      item[1] === "ncontains" ||
                      item[1] === "lt" ||
                      item[1] === "lte" ||
                      item[1] === "gt" ||
                      item[1] === "gte" ||
                      item[1] === "between" ||
                      item[1] === "nbetween" ||
                      item[1] === "starts" ||
                      item[1] === "nstarts" ||
                      item[1] === "ends" ||
                      item[1] === "nends"
                    ) {
                      return item[2] !== undefined || item[3] !== undefined;
                    } else {
                      return true;
                    }
                  });
                return {
                  filters,
                } as any;
              },
              () =>
                this.sortAndFilterReaction({dataView: rootDataView, updateTotalRowCount: true}),
              {
                equals: comparer.structural,
                delay: 100,
              }
            )
          );
          this.disposers.push(
            reaction(
              () => orderingConfiguration.userOrderings.map((x) => [x.columnId, x.direction]),
              () =>
                this.sortAndFilterReaction({dataView: rootDataView, updateTotalRowCount: false}),
              {
                equals: comparer.structural,
                delay: 100,
              }
            )
          );
        }
      }
    } finally {
      this.initialSelectedRowId = undefined;
    }
    yield*this.startAutoRefreshIfNeeded();
  }

  private initializePlugins(initUIResult: any) {
    let screenLevelPlugins = find(initUIResult.formDefinition, (node: any) => node.attributes?.Type === "ScreenLevelPlugin");
    let sessionId = getSessionId(this);
    screenLevelPlugins
      .forEach(node => {
        const plugin = pluginLibrary.get(
          {
            name: node.attributes.Name,
            modelInstanceId: node.attributes.ModelInstanceId,
            sessionId: sessionId
          });
        if (!isIScreenPlugin(plugin)) {
          throw new Error(`Plugin ${node.attributes.Name} is not ScreenLevelPlugin`)
        }
        plugin.requestSessionRefresh = () => runGeneratorInFlowWithHandler(
          {ctx: this, generator: this.refreshSession()}
        );
        plugin.setScreenParameters = (parameters: { [key: string]: string }) =>
          Object.keys(parameters)
            .forEach(key => this.parameters[key] = parameters[key]);
        plugin.initialize(node.attributes);
      })

    find(initUIResult.formDefinition, (node: any) => node.attributes?.Type === "SectionLevelPlugin")
      .forEach(node => {
        const plugin = pluginLibrary.get(
          {
            name: node.attributes.Name,
            modelInstanceId: node.attributes.ModelInstanceId,
            sessionId: sessionId
          })
        if (!isISectionPlugin(plugin)) {
          throw new Error(`Plugin ${node.attributes.Name} is not SectionLevelPlugin`)
        }
        plugin.getScreenParameters = () => _.cloneDeep(this.parameters);
        plugin.initialize(node.attributes)
      });
  }

  sortAndFilterReaction(args: { dataView: IDataView; updateTotalRowCount: boolean }) {
    const self = this;
    flow(function*() {
      if (!(yield shouldProceedToChangeRow(args.dataView))) {
        return;
      }
      yield args.dataView.lifecycle.runRecordChangedReaction(function*() {
        const groupingConfig = getGroupingConfiguration(args.dataView);
        if (groupingConfig.isGrouping) {
          args.dataView.serverSideGrouper.refresh();
        } else {
          args.dataView.setRowCount(undefined);
          yield self.readFirstChunkOfRowsWithGateDebounced(args.dataView);
          yield self.updateTotalRowCount(args.dataView);
        }
      });
    })();
  }

  *applyInitUIResult(args: { initUIResult: any }): any {
    const openedScreen = getOpenedScreen(this);

    assignIIds(args.initUIResult.formDefinition);

    const {formScreen: screen, foundLookupIds} = yield*interpretScreenXml(
      args.initUIResult.formDefinition,
      this,
      args.initUIResult.panelConfigurations,
      args.initUIResult.lookupMenuMappings,
      args.initUIResult.sessionId,
      args.initUIResult.workflowTaskId,
      openedScreen.lazyLoading
    );
    const api = getApi(openedScreen);
    const cacheDependencies = getWorkbench(openedScreen).lookupMultiEngine.cacheDependencies;
    const lookupIdsToQuery = cacheDependencies.getUnhandledLookupIds(foundLookupIds);

    if (lookupIdsToQuery.size > 0) {
      const dependencies = yield api.getLookupCacheDependencies({
        LookupIds: Array.from(lookupIdsToQuery),
      });
      cacheDependencies.putValues(dependencies);
    }

    openedScreen.content.setFormScreen(screen);
    // screen.printMasterDetailTree();
    yield*this.applyData(args.initUIResult.data);

    setTimeout(() => {
      const fieldToSelect = getFormScreen(this).getFirstFormPropertyId();
      if (fieldToSelect) {
        const formScreen = getFormScreen(this);
        const $formScreen = scopeFor(formScreen);
        $formScreen?.resolve(IScreenEvents).focusField.trigger({propertyId: fieldToSelect});
      }
    }, 100);
  }

  async loadChildRows(dataView: IDataView, filter: string, ordering: IOrdering | undefined) {
    try {
      this.monitor.inFlow++;
      const masterRowId =
        !this.eagerLoading && !dataView.isRootGrid && dataView.bindingParent
          ? dataView.bindingParent.selectedRowId
          : undefined;
      const api = getApi(this);
      return await api.getRows({
        MenuId: getMenuItemId(dataView),
        SessionFormIdentifier: getSessionId(this),
        DataStructureEntityId: getDataStructureEntityId(dataView),
        Filter: filter,
        FilterLookups: getUserFilterLookups(dataView),
        Ordering: ordering ? [ordering] : [],
        RowLimit: SCROLL_ROW_CHUNK,
        Parameters: this.parameters,
        MasterRowId: masterRowId,
        RowOffset: 0,
        ColumnNames: getColumnNamesToLoad(dataView),
      });
    } finally {
      this.monitor.inFlow--;
    }
  }

  async loadChildGroups(
    rootDataView: IDataView,
    filter: string,
    groupingSettings: IGroupingSettings,
    aggregations: IAggregationInfo[] | undefined,
    lookupId: string | undefined
  ) {
    const ordering = {
      columnId: groupingSettings.columnId,
      direction: IOrderByDirection.ASC,
      lookupId: lookupId,
    };
    try {
      this.monitor.inFlow++;
      const api = getApi(this);
      return await api.getGroups({
        MenuId: getMenuItemId(rootDataView),
        SessionFormIdentifier: getSessionId(this),
        DataStructureEntityId: getDataStructureEntityId(rootDataView),
        Filter: filter,
        FilterLookups: getUserFilterLookups(rootDataView),
        Ordering: [ordering],
        RowLimit: 999999,
        GroupBy: groupingSettings.columnId,
        GroupingUnit: groupingUnitToString(groupingSettings.groupingUnit),
        GroupByLookupId: lookupId,
        MasterRowId: undefined,
        AggregatedColumns: aggregations,
      });
    } finally {
      this.monitor.inFlow--;
    }
  }

  async loadGroups(
    dataView: IDataView,
    columnSettings: IGroupingSettings,
    groupByLookupId: string | undefined,
    aggregations: IAggregationInfo[] | undefined
  ) {
    const orderingConfig = getOrderingConfiguration(dataView);
    const orderingDirection =
      orderingConfig.orderings.find((ordering) => ordering.columnId === columnSettings.columnId)
        ?.direction ?? IOrderByDirection.ASC;

    const api = getApi(this);
    const ordering = {
      columnId: columnSettings.columnId,
      direction: orderingDirection,
      lookupId: groupByLookupId,
    };

    const masterRowId =
      !this.eagerLoading && !dataView.isRootGrid && dataView.bindingParent
        ? dataView.bindingParent.selectedRowId
        : undefined;

    try {
      this.monitor.inFlow++;
      return await api.getGroups({
        MenuId: getMenuItemId(dataView),
        SessionFormIdentifier: getSessionId(this),
        DataStructureEntityId: getDataStructureEntityId(dataView),
        Filter: getUserFilters({ctx: dataView}),
        FilterLookups: getUserFilterLookups(dataView),
        Ordering: [ordering],
        RowLimit: 999999,
        GroupBy: columnSettings.columnId,
        GroupingUnit: groupingUnitToString(columnSettings.groupingUnit),
        GroupByLookupId: groupByLookupId,
        MasterRowId: masterRowId,
        AggregatedColumns: aggregations,
      });
    } finally {
      this.monitor.inFlow--;
    }
  }

  loadAggregations(rootDataView: IDataView, aggregations: IAggregationInfo[]) {
    const api = getApi(this);
    return api.getAggregations({
      MenuId: getMenuItemId(rootDataView),
      SessionFormIdentifier: getSessionId(this),
      DataStructureEntityId: getDataStructureEntityId(rootDataView),
      Filter: getUserFilters({ctx: rootDataView}),
      FilterLookups: getUserFilterLookups(rootDataView),
      MasterRowId: undefined,
      AggregatedColumns: aggregations,
    });
  }

  *loadData() {
    const formScreen = getFormScreen(this);
    try {
      this.monitor.inFlow++;
      for (let dataView of formScreen.nonRootDataViews) {
        dataView.dataTable.clear();
        dataView.lifecycle.stopSelectedRowReaction();
      }
      for (let rootDataView of formScreen.rootDataViews) {
        rootDataView.saveViewState();
        const groupingConfiguration = getGroupingConfiguration(rootDataView);
        if (groupingConfiguration.isGrouping) {
          rootDataView.serverSideGrouper.refresh();
        } else {
          yield this.updateTotalRowCount(rootDataView);
          yield*this.readFirstChunkOfRows({
            rootDataView: rootDataView
          });
        }
      }
    } finally {
      for (let dataView of formScreen.nonRootDataViews) {
        dataView.lifecycle.startSelectedRowReaction();
      }
      this.monitor.inFlow--;
    }
  }

  *throwChangesAway(dataView: IDataView): any {
    try {
      this.monitor.inFlow++;
      const api = getApi(this);
      const updateObjectResult = yield api.restoreData({
        SessionFormIdentifier: getSessionId(this),
        ObjectId: dataView.selectedRowId!,
      });
      yield*processCRUDResult(dataView, updateObjectResult, false, dataView);
      const formScreen = getFormScreen(this);
      if (formScreen.requestSaveAfterUpdate) {
        yield*this.saveSession();
      }
    } finally {
      this.monitor.inFlow--;
    }
  }

  *flushData() {
    try {
      this.monitor.inFlow++;
      let updateObjectDidRun = false;
      const formScreen = getFormScreen(this);
      const dataViews = formScreen.dataViews;
      for (let dataView of dataViews) {
        updateObjectDidRun = updateObjectDidRun || (yield*this.runUpdateObject(dataView));
      }
      if (formScreen.requestSaveAfterUpdate && updateObjectDidRun) {
        yield*this.saveSession();
      }
    } finally {
      this.monitor.inFlow--;
    }
  }

  _processedUpdateObjectResults = new WeakSet<any>();

  private*runUpdateObject(dataView: IDataView): any {
    const updateData = dataView.dataTable.getDirtyValueRows().map((row) => {
      return {
        RowId: dataView.dataTable.getRowId(row),
        Values: map2obj(dataView.dataTable.getDirtyValues(row)),
      };
    });
    if (!updateData || updateData.length === 0) {
      return false;
    }
    const updateObjectResult = yield this.updateRequestAggregator.enqueue({
      SessionFormIdentifier: getSessionId(this),
      Entity: dataView.entity,
      UpdateData: updateData,
    });
    if (updateObjectResult === null){
      return false;
    }
    dataView.formFocusManager.stopAutoFocus();

    // This might run more times in parallel, but we want to apply the result just once.
    // Parallel promises will be resolved all by the same result of merged update request.
    if (!this._processedUpdateObjectResults.has(updateObjectResult)) {
      this._processedUpdateObjectResults.add(updateObjectResult);
      yield*processCRUDResult(dataView, updateObjectResult, false, dataView);
    }
    dataView.formFocusManager.refocusLast();

    return true;
  }

  *updateRadioButtonValue(dataView: IDataView, row: any, fieldName: string, newValue: string): any {
    try {
      this.monitor.inFlow++;
      const changes: any = {};
      changes[fieldName] = newValue;
      const formScreen = getFormScreen(this);
      const self = this;
      const updateObjectResult = yield self.updateRequestAggregator.enqueue({
        SessionFormIdentifier: getSessionId(self),
        Entity: dataView.entity,
        UpdateData: [
          {
            RowId: dataView.dataTable.getRowId(row),
            Values: changes,
          },
        ],
      });
      if (updateObjectResult === null){
        return;
      }
      yield*processCRUDResult(dataView, updateObjectResult, false, dataView);

      if (formScreen.requestSaveAfterUpdate) {
        yield*this.saveSession();
      }
    } finally {
      this.monitor.inFlow--;
    }
  }

  rowSelectedReactionsDisabled(dataView: IDataView) {
    if (this.initialSelectedRowId && dataView.isBindingRoot && !this.eagerLoading) {
      return true;
    }
    return false;
  }

  *getRowsForWorkQueue(rootDataView: IDataView): Generator {
    let columnNamesToLoad = getColumnNamesToLoad(rootDataView);
    let propertyIds = rootDataView.properties.map(prop => prop.id);
    const columnIndicesToExclude = columnNamesToLoad
      .filter(column => !propertyIds.includes(column) && column !== "Id")
      .map(column => columnNamesToLoad.indexOf(column));

    for (const column of Array.from(columnNamesToLoad)) {
      if(!propertyIds.includes(column) && column !== "Id"){
        const index = columnNamesToLoad.indexOf(column);
        columnNamesToLoad.splice(index, 1)
      }
    }

    const api = getApi(this);
    const loadedData = yield api.getRows({
      MenuId: getMenuItemId(rootDataView),
      SessionFormIdentifier: getSessionId(this),
      DataStructureEntityId: getDataStructureEntityId(rootDataView),
      Filter: getUserFilters({ctx: rootDataView}),
      FilterLookups: getUserFilterLookups(rootDataView),
      Ordering: getUserOrdering(rootDataView),
      RowLimit: SCROLL_ROW_CHUNK,
      MasterRowId: undefined,
      Parameters: this.parameters,
      RowOffset: 0,
      ColumnNames: columnNamesToLoad,
    });
    for (let row of loadedData as any[][]) {
      for (let index of columnIndicesToExclude) {
        row.splice(index,0, null)
      }
    }
    return loadedData;
  }

  *readFirstChunkOfRows(args: {
    rootDataView: IDataView
  }): any {
    const rootDataView = args.rootDataView;
    const api = getApi(this);
    rootDataView.setSelectedRowId(undefined);
    rootDataView.lifecycle.stopSelectedRowReaction();
    try {
      this.monitor.inFlow++;
      const openedScreen = getOpenedScreen(rootDataView);
      let loadedData;
      if(openedScreen.menuItemType === IMainMenuItemType.WorkQueue){
        loadedData = yield*this.getRowsForWorkQueue(rootDataView);
      }
      else
      {
        loadedData = yield api.getRows({
          MenuId: getMenuItemId(rootDataView),
          SessionFormIdentifier: getSessionId(this),
          DataStructureEntityId: getDataStructureEntityId(rootDataView),
          Filter: getUserFilters({ctx: rootDataView}),
          FilterLookups: getUserFilterLookups(rootDataView),
          Ordering: getUserOrdering(rootDataView),
          RowLimit: SCROLL_ROW_CHUNK,
          MasterRowId: undefined,
          Parameters: this.parameters,
          RowOffset: 0,
          ColumnNames: getColumnNamesToLoad(rootDataView),
        });
      }
      yield rootDataView.setRecords(loadedData);
      if (this.initialSelectedRowId) {
        rootDataView.setSelectedRowId(this.initialSelectedRowId);
      } else {
        rootDataView.reselectOrSelectFirst();
      }
      rootDataView.restoreViewState();
    } finally {
      rootDataView.lifecycle.startSelectedRowReaction(true);
      this.monitor.inFlow--;
    }
  }

  _readFirstChunkOfRowsRunning = false;
  _readFirstChunkOfRowsScheduled = false;

  *readFirstChunkOfRowsWithGate(rootDataView: IDataView) {
    try {
      if (this._readFirstChunkOfRowsRunning) {
        this._readFirstChunkOfRowsScheduled = true;
        return;
      }
      this._readFirstChunkOfRowsRunning = true;
      do {
        this._readFirstChunkOfRowsScheduled = false;
        yield*this.readFirstChunkOfRows({
          rootDataView: rootDataView
        });
      } while (this._readFirstChunkOfRowsScheduled);
    } finally {
      this._readFirstChunkOfRowsRunning = false;
      this._readFirstChunkOfRowsScheduled = false;
    }
  }

  readFirstChunkOfRowsWithGateDebounced = _.debounce(
    flow(this.readFirstChunkOfRowsWithGate.bind(this)),
    500
  );

  private getNewRowValues(targetDataView: IDataView) {
    if (!targetDataView.orderMember) {
      return;
    }
    const orderMember = targetDataView.orderMember;
    const dataSourceField = getDataSourceFieldByName(targetDataView, orderMember);
    const orderValues = targetDataView.tableRows
      .map((row) => (row as any[])[dataSourceField!.index] as number);
    const nextOrderValue = orderValues.length > 0 ? Math.max(...orderValues) + 1 : 1;
    const values = {} as any;
    values[orderMember] = nextOrderValue;
    return values;
  }

  *createRow(entity: string, gridId: string): any {
    try {
      this.monitor.inFlow++;
      const api = getApi(this);
      const targetDataView = getDataViewByGridId(this, gridId)!;
      const formScreen = getFormScreen(this);
      let createObjectResult;
      try {
        yield*formScreen.dataUpdateCRS.enterGenerator();
        createObjectResult = yield api.createObject({
          SessionFormIdentifier: getSessionId(this),
          Entity: entity,
          RequestingGridId: gridId,
          Values: this.getNewRowValues(targetDataView),
          Parameters: {...getBindingParametersFromParent(targetDataView)},
        });
      } finally {
        formScreen.dataUpdateCRS.leave();
      }
      yield*processCRUDResult(targetDataView, createObjectResult, false, targetDataView);
      if (targetDataView.newRecordView === "0" && targetDataView.activateFormView) {
        yield targetDataView.activateFormView({saveNewState: true});
      } else {
        if (targetDataView.isTableViewActive()) {
          yield*startEditingFirstCell(targetDataView)();
        } else if (targetDataView.isFormViewActive()) {
          getFormFocusManager(targetDataView).forceAutoFocus();
        }
      }
    } finally {
      this.monitor.inFlow--;
    }
  }

  *copyRow(entity: string, gridId: string, rowId: string): any {
    try {
      this.monitor.inFlow++;
      const targetDataView = getDataViewByGridId(this, gridId)!;
      const childEntities = getAllBindingChildren(targetDataView)
        .filter(dataView => getIsAddButtonVisible(dataView))
        .map((dataView) => getEntity(dataView)
        );

      const api = getApi(this);
      const formScreen = getFormScreen(this);
      let createObjectResult;
      try {
        yield*formScreen.dataUpdateCRS.enterGenerator();
        createObjectResult = yield api.copyObject({
          SessionFormIdentifier: getSessionId(this),
          Entity: entity,
          OriginalId: rowId,
          RequestingGridId: gridId,
          Entities: [entity, ...childEntities],
          ForcedValues: this.getNewRowValues(targetDataView),
        });
      } finally {
        formScreen.dataUpdateCRS.leave();
      }
      yield*processCRUDResult(targetDataView, createObjectResult, false, targetDataView);
      getTablePanelView(targetDataView)!.scrollToCurrentRow();
    } finally {
      this.monitor.inFlow--;
    }
  }

  *deleteRow(entity: string, rowId: string, targetDataView: IDataView): any {
    try {
      this.monitor.inFlow++;
      const api = getApi(this);
      const formScreen = getFormScreen(this);
      let deleteObjectResult;
      try {
        yield*formScreen.dataUpdateCRS.enterGenerator();

        if (targetDataView.orderMember) {
          deleteObjectResult = yield*this.deleteObjectInOrderedList(rowId, entity, targetDataView);
        } else {
          deleteObjectResult = yield api.deleteObject({
            SessionFormIdentifier: getSessionId(this),
            Entity: entity,
            Id: rowId,
          });
        }
      } finally {
        formScreen.dataUpdateCRS.leave();
      }
      yield*processCRUDResult(this, deleteObjectResult);
    } finally {
      this.monitor.inFlow--;
    }
  }

  private*deleteObjectInOrderedList(rowId: string, entity: string, targetDataView: IDataView): any {
    const api = getApi(this);
    const rowToDelete = targetDataView.dataTable.getRowById(rowId)!;
    const orderMember = targetDataView.orderMember;
    const newRowOrderMap = {} as any;
    if (orderMember) {
      const dataSourceField = getDataSourceFieldByName(targetDataView, orderMember)!;
      targetDataView.dataTable.rows
        .filter((row) => row[dataSourceField.index] > rowToDelete[dataSourceField.index])
        .forEach((row) => {
          const rowId = targetDataView.dataTable.getRowId(row);
          const newOrder = row[dataSourceField.index] - 1;
          newRowOrderMap[rowId] = newOrder;
        });

      return yield api.deleteObjectInOrderedList({
        SessionFormIdentifier: getSessionId(this),
        Entity: entity,
        Id: rowId,
        OrderProperty: orderMember,
        UpdatedOrderValues: newRowOrderMap,
      });
    }
  }

  *saveSession(): any {
    if (getIsSuppressSave(this)) {
      return true;
    }
    try {
      this.monitor.inFlow++;
      const api = getApi(this);
      let result;
      const formScreen = getFormScreen(this);
      try {
        yield*formScreen.dataUpdateCRS.enterGenerator();
        const queryResult = yield api.saveSessionQuery(getSessionId(this));

        const processQueryInfoResult = yield*processActionQueryInfo(this)(
          queryResult,
          formScreen.title
        );
        if (!processQueryInfoResult.canContinue) {
          return false;
        }
        result = yield api.saveSession(getSessionId(this));
        getFormScreen(this).dataViews.forEach((dataView) =>
          dataView.dataTable.unlockAddedRowPosition()
        );
      } finally {
        formScreen.dataUpdateCRS.leave();
      }
      yield*refreshWorkQueues(this)();
      yield*processCRUDResult(this, result);
      getFormScreen(this).dataViews.forEach((dataView) =>
        dataView.dataTable.updateSortAndFilter({retainPreviousSelection: true})
      );
      yield*this.updateTotalRowCounts();
      // getFormScreen(this).dataViews.forEach((dataView) =>
      //     yield dataView.reloadAggregations()
      // );
    } finally {
      this.monitor.inFlow--;
    }
    return true;
  }

  *refreshLookups() {
    const dataViews = getDataViewList(this);
    const properties = dataViews.flatMap((dv) => getProperties(dv)).filter((prop) => prop.isLookup);
    const cleaned = new Set<any>();
    for (let prop of properties) {
      if (prop.lookupEngine && !cleaned.has(prop.lookupId)) {
        prop.lookupEngine.cleanAndReload();
        getWorkbench(this).lookupListCache.deleteLookup(prop.lookupId!);
        cleaned.add(prop.lookupId);
      }
    }
  }

  *refreshSession(): any {
    // TODO: Refresh lookups and rowStates !!!
    try {
      this.monitor.inFlow++;
      if (this.eagerLoading) {
        const formScreen = getFormScreen(this);
        formScreen.dataViews.forEach((dv) => dv.saveViewState());
        const api = getApi(this);
        let result;
        try {
          yield*formScreen.dataUpdateCRS.enterGenerator();
          result = yield api.refreshSession(getSessionId(this));
        } finally {
          formScreen.dataUpdateCRS.leave();
        }
        yield*this.applyData(result);
        getFormScreen(this).setDirty(false);
        getFormScreen(this).dataViews.forEach((dv) => dv.restoreViewState());
      } else {
        yield*this.loadData();
      }
      getFormScreen(this).setDirty(false);
      yield*this.refreshLookups();
      getFormScreen(this).clearDataCache();
    } finally {
      this.monitor.inFlow--;
      setTimeout(async () => {
        await when(() => this.allDataViewsSteady);
      }, 10);
    }
    yield*refreshRowStates(this)();
    yield*refreshWorkQueues(this)();
    pluginLibrary.notifyRefresh();
  }

  loadInitialData() {
    if (!this.eagerLoading) {
      const self = this;
      flow(function*() {
        yield*self.clearTotalCounts();
        yield*self.loadData();
        yield*self.updateTotalRowCounts();
      })();
    }
  }

  *clearTotalCounts() {
    const formScreen = getFormScreen(this);
    for (const dataView of formScreen.rootDataViews) {
      if (isInfiniteScrollingActive(dataView) && !getGroupingConfiguration(dataView).isGrouping) {
        dataView.setRowCount(undefined);
      }
    }
  }

  *updateTotalRowCounts() {
    const formScreen = getFormScreen(this);
    for (const dataView of formScreen.rootDataViews) {
      if (isInfiniteScrollingActive(dataView) && !getGroupingConfiguration(dataView).isGrouping) {
        yield this.updateTotalRowCount(dataView);
        yield this.reloadAggregations(dataView);
      }
    }
  }

  async reloadAggregations(dataView: IDataView) {
    const aggregations = getTablePanelView(dataView)!.aggregations.aggregationList;
    if (aggregations.length === 0) {
      dataView.aggregationData.length = 0;
      return;
    }
    if (isInfiniteScrollingActive(dataView)) {
      const data = await getFormScreenLifecycle(dataView).loadAggregations(dataView, aggregations);
      dataView.aggregationData = parseAggregations(data) || [];
    } else {
      dataView.aggregationData = calcAggregations(dataView, aggregations);
    }
  }

  async updateTotalRowCount(dataView: IDataView) {
    const api = getApi(this);
    const aggregationResult = await api.getAggregations({
      MenuId: getMenuItemId(dataView),
      SessionFormIdentifier: getSessionId(this),
      DataStructureEntityId: getDataStructureEntityId(dataView),
      Filter: getUserFilters({ctx: dataView}),
      FilterLookups: getUserFilterLookups(dataView),
      MasterRowId: undefined,
      AggregatedColumns: [
        {
          ColumnName: "Id",
          AggregationType: AggregationType.COUNT,
        },
      ],
    });

    const aggregationData = parseAggregations(aggregationResult);
    if (aggregationData && aggregationData.length > 0) {
      dataView.setRowCount(aggregationData[0].value);
    }
  }

  private actionRunning = false;

  *executeAction(gridId: string, entity: string, action: IAction, selectedItems: string[]): any {
    if (this.actionRunning) {
      return;
    }
    this.actionRunning = true;
    try {
      this.monitor.inFlow++;
      const parameters: { [key: string]: any } = {};
      for (let parameter of action.parameters) {
        parameters[parameter.name] = parameter.fieldName;
      }
      const api = getApi(this);
      const formScreen = getFormScreen(this);
      let result;
      try {
        yield*formScreen.dataUpdateCRS.enterGenerator();
        const queryResult = (yield api.executeActionQuery({
          SessionFormIdentifier: getSessionId(this),
          Entity: entity,
          ActionType: action.type,
          ActionId: action.id,
          ParameterMappings: parameters,
          SelectedIds: selectedItems,
          InputParameters: {},
        })) as IQueryInfo[];
        const processQueryInfoResult = yield*processActionQueryInfo(this)(
          queryResult,
          formScreen.title
        );
        if (!processQueryInfoResult.canContinue) return;

        const self = this;
        result = yield api.executeAction({
          SessionFormIdentifier: getSessionId(self),
          Entity: entity,
          ActionType: action.type,
          ActionId: action.id,
          ParameterMappings: parameters,
          SelectedIds: selectedItems,
          InputParameters: {},
          RequestingGrid: gridId,
        });
      } finally {
        formScreen.dataUpdateCRS.leave();
      }

      yield*new_ProcessActionResult(action)(result);
      yield*refreshRowStates(this)();
      const dataView = getDataView(action);
      dataView?.formFocusManager?.refocusLast();
    } finally {
      this.monitor.inFlow--;
      this.actionRunning = false;
    }
  }

  @action.bound
  killForm() {
    this.clearAutoRefreshInterval();
    this.disposers.forEach((disposer) => disposer());
    getDataViewList(this).forEach((dv) => dv.stop());
    const openedScreen = getOpenedScreen(this);
    openedScreen.content.setFormScreen(undefined);
  }

  *closeForm() {
    try {
      this.monitor.inFlow++;
      yield*closeForm(this)();
      this.killForm();
    } finally {
      this.monitor.inFlow--;
    }
  }

  questionSaveData() {
    return new Promise(
      action((resolve: (value: IQuestionSaveDataAnswer) => void) => {
        const closeDialog = showDialog(this,
          "",
          <QuestionSaveData
            screenTitle={getOpenedScreen(this).tabTitle}
            onSaveClick={() => {
              closeDialog();
              resolve(IQuestionSaveDataAnswer.Save);
            }}
            onDontSaveClick={() => {
              closeDialog();
              resolve(IQuestionSaveDataAnswer.NoSave);
            }}
            onCancelClick={() => {
              closeDialog();
              resolve(IQuestionSaveDataAnswer.Cancel);
            }}
          />
        );
      })
    );
  }

  questionDeleteData() {
    return new Promise(
      action((resolve: (value: IQuestionDeleteDataAnswer) => void) => {
        const closeDialog = showDialog(this,
          "",
          <YesNoQuestion
            screenTitle={getOpenedScreen(this).tabTitle}
            yesLabel={T("Yes", "button_yes")}
            noLabel={T("No", "button_no")}
            message={T("Delete selected row?", "delete_confirmation")}
            onNoClick={() => {
              closeDialog();
              resolve(IQuestionDeleteDataAnswer.No);
            }}
            onYesClick={() => {
              closeDialog();
              resolve(IQuestionDeleteDataAnswer.Yes);
            }}
          />
        );
      })
    );
  }

  *applyData(data: any): Generator {
    for (let [entityKey, entityValue] of Object.entries(data || {})) {
      const dataViews = getDataViewsByEntity(this, entityKey);
      for (let dataView of dataViews) {
        yield dataView.setRecords((entityValue as any).data);
        dataView.setRowCount(dataView.dataTable.rows.length);
        yield dataView.start();

        const reloadAggregationsDebounced = _.debounce(() => {
          const self = this;
          runInFlowWithHandler({
            ctx: dataView,
            action: () => self.reloadAggregations(dataView),
          });
        }, 10);

        this.registerDisposer(
          reaction(
            () => {
              const tablePanelView = getTablePanelView(dataView);
              if (!tablePanelView) return [];
              return [
                [...tablePanelView.aggregations.aggregationList],
                getFilterConfiguration(dataView).activeFilters.map((x) => [
                  x.propertyId,
                  x.setting.type,
                  x.setting.val1,
                  x.setting.val2,
                ]),
                isInfiniteScrollingActive(dataView) ? true : dataView.dataTable.rows.length === 0,
              ];
            },
            () => reloadAggregationsDebounced(),
            {fireImmediately: true}
          )
        );
        this.registerDisposer(
          reaction(
            () => isMobileLayoutActive(dataView),
            () => {
              const tablePanelView = getTablePanelView(dataView);
              const configurationManager = getConfigurationManager(tablePanelView);
              configurationManager.activeTableConfiguration.apply(tablePanelView!);
            },
            {fireImmediately: true}
          )
        );
      }
    }
  }

  get eagerLoading() {
    return !isLazyLoading(this);
  }

  parent?: any;
}
