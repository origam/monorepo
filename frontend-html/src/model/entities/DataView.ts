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

import { action, computed, observable, reaction } from "mobx";
import { getParentRow } from "model/selectors/DataView/getParentRow";
import { getSelectedRowId } from "model/selectors/TablePanelView/getSelectedRowId";
import { getDataSourceByEntity } from "model/selectors/DataSources/getDataSourceByEntity";
import { getDataTable } from "model/selectors/DataView/getDataTable";
import { getFormScreen } from "model/selectors/FormScreen/getFormScreen";
import { getIsDialog } from "model/selectors/getIsDialog";
import { IDataViewLifecycle } from "model/entities/DataViewLifecycle/types/IDataViewLifecycle";
import { IFormPanelView } from "model/entities/FormPanelView/types/IFormPanelView";
import { ITablePanelView } from "model/entities/TablePanelView/types/ITablePanelView";
import { IAction, IActionPlacement, IActionType } from "model/entities/types/IAction";
import { IDataTable } from "model/entities/types/IDataTable";
import { IDataView, IDataViewData } from "model/entities/types/IDataView";
import { IPanelViewType } from "model/entities/types/IPanelViewType";
import { IProperty } from "model/entities/types/IProperty";
import { getBindingToParent } from "model/selectors/DataView/getBindingToParent";
import { getDataSourceFieldByName } from "model/selectors/DataSources/getDataSourceFieldByName";
import { getBindingParent } from "model/selectors/DataView/getBindingParent";
import { ILookupLoader } from "model/entities/types/ILookupLoader";
import bind from "bind-decorator";
import { getRowStateMayCauseFlicker } from "model/selectors/RowState/getRowStateMayCauseFlicker";
import { getTablePanelView } from "model/selectors/TablePanelView/getTablePanelView";
import { getSelectedRow } from "model/selectors/DataView/getSelectedRow";
import { ServerSideGrouper } from "model/entities/ServerSideGrouper";
import { ClientSideGrouper } from "model/entities/ClientSideGrouper";
import { getFormScreenLifecycle } from "model/selectors/FormScreen/getFormScreenLifecycle";
import { getTableViewProperties } from "model/selectors/TablePanelView/getTableViewProperties";
import { getIsSelectionCheckboxesShown } from "model/selectors/DataView/getIsSelectionCheckboxesShown";
import { getGroupingConfiguration } from "model/selectors/TablePanelView/getGroupingConfiguration";
import { flattenToTableRows } from "gui/Components/ScreenElements/Table/TableRendering/tableRows";
import { GridDimensions } from "gui/Workbench/ScreenArea/TableView/GridDimensions";
import { SimpleScrollState } from "gui/Components/ScreenElements/Table/SimpleScrollState";
import { BoundingRect } from "react-measure";
import { IGridDimensions } from "gui/Components/ScreenElements/Table/types";
import { FormFocusManager } from "model/entities/FormFocusManager";
import { getRowStates } from "model/selectors/RowState/getRowStates";
import { getLookupLoader } from "model/selectors/DataView/getLookupLoader";
import { DataViewData } from "modules/DataView/DataViewData";
import { DataViewAPI } from "modules/DataView/DataViewAPI";
import { RowCursor } from "modules/DataView/TableCursor";
import { isLazyLoading } from "model/selectors/isLazyLoading";
import {
  IInfiniteScrollLoader,
  InfiniteScrollLoader,
  NullIScrollLoader,
} from "gui/Workbench/ScreenArea/TableView/InfiniteScrollLoader";
import { ScrollRowContainer } from "model/entities/ScrollRowContainer";
import { VisibleRowsMonitor } from "gui/Workbench/ScreenArea/TableView/VisibleRowsMonitor";
import { getSelectionMember } from "model/selectors/DataView/getSelectionMember";
import { getApi } from "model/selectors/getApi";
import { getSessionId } from "model/selectors/getSessionId";
import { getGrouper } from "model/selectors/DataView/getGrouper";
import { getUserFilters } from "model/selectors/DataView/getUserFilters";
import { getMenuItemId } from "model/selectors/getMenuItemId";
import { getDataStructureEntityId } from "model/selectors/DataView/getDataStructureEntityId";
import { getUserOrdering } from "model/selectors/DataView/getUserOrdering";
import { getColumnNamesToLoad } from "model/selectors/DataView/getColumnNamesToLoad";
import { getUserFilterLookups } from "model/selectors/DataView/getUserFilterLookups";
import { isInfiniteScrollingActive } from "model/selectors/isInfiniteScrollingActive";
import { getPropertyOrdering } from "model/selectors/DataView/getPropertyOrdering";
import { IOrderByDirection } from "model/entities/types/IOrderingConfiguration";

import selectors from "model/selectors-tree";
import produce from "immer";
import { getDataSourceFieldIndexByName } from "model/selectors/DataSources/getDataSourceFieldIndexByName";
import { onMainMenuItemClick } from "model/actions-ui/MainMenu/onMainMenuItemClick";
import { onSelectedRowChange } from "model/actions-ui/onSelectedRowChange";
import { runInFlowWithHandler, } from "utils/runInFlowWithHandler";
import { IAggregation } from 'model/entities/types/IAggregation';
import { getConfigurationManager } from "model/selectors/TablePanelView/getConfigurationManager";
import { GridFocusManager } from "model/entities/GridFocusManager";
import { ScreenFocusManager } from "model/entities/ScreenFocusManager";

class SavedViewState {
  constructor(public selectedRowId: string | undefined) {
  }
}

export class DataView implements IDataView {
  $type_IDataView: 1 = 1;
  formFocusManager: FormFocusManager = new FormFocusManager(this);
  gridFocusManager: GridFocusManager = new GridFocusManager(this);

  @observable aggregationData: IAggregation[] = [];

  constructor(data: IDataViewData) {
    Object.assign(this, data);
    this.properties.forEach((o) => (o.parent = this));
    this.actions.forEach((o) => (o.parent = this));
    this.defaultActions = this.actions.filter((action) => action.isDefault);
    this.dataTable.parent = this;
    this.lifecycle.parent = this;
    this.tablePanelView.parent = this;
    this.formPanelView.parent = this;
    this.lookupLoader.parent = this;
    this.clientSideGrouper.parent = this;
    this.serverSideGrouper.parent = this;
    this.focusManager.registerGridFocusManager(this.gridFocusManager);
    this.focusManager.registerFormFocusManager(this.formFocusManager);

    this.gridDimensions = new GridDimensions({
      getTableViewProperties: () => getTableViewProperties(this),
      getRowCount: () => this.tableRows.length,
      getIsSelectionCheckboxes: () =>
        getIsSelectionCheckboxesShown(this.tablePanelView),
      ctx: this,
      defaultRowHeight: this.tablePanelView.rowHeight,
    });

    this.orderProperty = this.properties.find(
      (prop) => prop.id === this.orderMember
    )!;
    this.dataTable.rowRemovedListeners.push(
      () => (this.selectAllCheckboxChecked = false)
    );
  }

  private _isFormViewActive = () => false;

  set isFormViewActive(value: () => boolean) {
    this._isFormViewActive = value;
  }

  get isFormViewActive() {
    return this._isFormViewActive;
  }

  private _isTableViewActive = () => false;

  set isTableViewActive(value: () => boolean) {
    this._isTableViewActive = value;
  }

  get isTableViewActive() {
    return this._isTableViewActive;
  }

  @action.bound
  setRowCount(rowCount: number) {
    this.rowCount = rowCount;
    this.dataTable.rowsAddedSinceSave = 0;
  }

  @observable
  rowCount: number | undefined;

  @computed
  get totalRowCount() {
    if (!this.rowCount) {
      return this.rowCount;
    }
    return this.rowCount + this.dataTable.rowsAddedSinceSave;
  }

  orderProperty: IProperty;
  activateFormView:
    | ((args?: { saveNewState: boolean }) => Promise<any>)
    | undefined;
  activateTableView: (() => Promise<any>) | undefined;

  gridDimensions: IGridDimensions;

  id = "";
  modelInstanceId = "";
  name = "";
  modelId = "";
  defaultPanelView = IPanelViewType.Table;
  isHeadless = false;
  isMapSupported = false;
  disableActionButtons = false;
  showAddButton = false;
  hideCopyButton = false;
  showDeleteButton = false;
  showSelectionCheckboxesSetting = false;
  isGridHeightDynamic = false;
  selectionMember = "";
  orderMember = "";
  isDraggingEnabled = false;
  entity = "";
  dataMember = "";
  isRootGrid = false;
  isRootEntity = false;
  isPreloaded = false;
  newRecordView: string | undefined;
  requestDataAfterSelectionChange = false;
  confirmSelectionChange = false;
  properties: IProperty[] = [];
  actions: IAction[] = [];
  defaultActions: IAction[] = [];
  type: string = "";
  focusManager: ScreenFocusManager = null as any;
  @observable tableViewProperties: IProperty[] = [];
  dataTable: IDataTable = null as any;
  formViewUI: any;
  lifecycle: IDataViewLifecycle = null as any;
  tablePanelView: ITablePanelView = null as any;
  formPanelView: IFormPanelView = null as any;
  lookupLoader: ILookupLoader = null as any;
  serverSideGrouper: ServerSideGrouper = null as any;
  clientSideGrouper: ClientSideGrouper = null as any;
  isFirst: boolean = null as any;

  dataViewRowCursor: RowCursor = null as any;
  dataViewApi: DataViewAPI = null as any;
  dataViewData: DataViewData = null as any;

  @observable selectAllCheckboxChecked = false;
  @observable selectedRowIds: Set<string> = new Set();

  @observable activePanelView: IPanelViewType = IPanelViewType.Table;

  @observable selectedRowId: string | undefined;

  @computed get showSelectionCheckboxes() {
    return this.showSelectionCheckboxesSetting || !!this.selectionMember;
  }

  @computed get firstEnabledDefaultAction() {
    return this.defaultActions.find((action) => action.isEnabled);
  }

  @bind hasSelectedRowId(id: string) {
    return this.selectedRowIds.has(id);
  }

  appendRecords(rows: any[][]): void {
    this.dataTable.appendRecords(rows);
    this.selectedRowIds.clear();
    this.selectAllCheckboxChecked =
      this.dataTable.rows.length !== 0 &&
      this.dataTable.rows.every((row) =>
        this.isSelected(this.dataTable.getRowId(row))
      );
  }

  async setRecords(rows: any[][]): Promise<any> {
    await this.dataTable.setRecords(rows);
    const filteredRows = this.dataTable.rows;
    const filteredRowIds = filteredRows.map((row) =>
      this.dataTable.getRowId(row)
    );
    this.selectedRowIds = new Set(
      Array.from(this.selectedRowIds).filter((id) =>
        filteredRowIds.includes(id)
      )
    );
    this.selectAllCheckboxChecked =
      this.dataTable.rows.length !== 0 &&
      this.dataTable.rows.every((row) =>
        this.isSelected(this.dataTable.getRowId(row))
      );
  }

  isSelected(rowId: string): boolean {
    const selectionMember = getSelectionMember(this);
    if (!!selectionMember) {
      const dataSourceField = getDataSourceFieldByName(this, selectionMember)!;
      if (!dataSourceField) {
        throw new Error(
          `SelectionMember "${selectionMember}" was not found in data source. Make sure the SelectionMember value in ${this.modelInstanceId} is correct`
        );
      }
      const updatedRow = this.dataTable.getRowById(rowId)!;
      return this.dataTable.getCellValueByDataSourceField(
        updatedRow,
        dataSourceField
      );
    }
    return this.selectedRowIds.has(rowId);
  }

  @action.bound addSelectedRowId(id: string) {
    this.selectedRowIds.add(id);
  }

  @action.bound removeSelectedRowId(id: string) {
    this.selectedRowIds.delete(id);
    this.selectAllCheckboxChecked = false;
  }

  @action.bound
  clear() {
    this.selectedRowIds.clear();
    this.dataTable.clear();
  }

  @action.bound
  deleteRowAndSelectNext(row: any[]) {
    const id = this.dataTable.getRowId(row);
    let idToSelectNext = this.dataTable.getNextExistingRowId(id);
    if (!idToSelectNext) {
      idToSelectNext = this.dataTable.getPrevExistingRowId(id);
    }

    this.selectedRowIds.delete(id);
    this.dataTable.deleteRow(row);

    this.setSelectedRowId(idToSelectNext);
  }

  @action.bound
  substituteRecord(row: any[]) {
    this.dataTable.substituteRecord(row);
    if (getGroupingConfiguration(this).isGrouping) {
      getGrouper(this).substituteRecord(row);
    }
  }

  @action.bound setSelectedState(rowId: string, newState: boolean) {
    if (newState) {
      this.addSelectedRowId(rowId);
    } else {
      this.removeSelectedRowId(rowId);
    }
  }

  getRowIndexById(rowId: any) {
    if (getGroupingConfiguration(this).isGrouping) {
      return getGrouper(this).allGroups.some((group) => group.isExpanded)
        ? getGrouper(this).getRowIndex(rowId)
        : undefined;
    } else {
      return (rowId !== null && rowId !== undefined)
        ? this.dataTable.getExistingRowIdxById(rowId)
        : undefined;
    }
  }

  @computed get selectedRowIndex(): number | undefined {
    return this.getRowIndexById(this.selectedRowId)
  }

  @computed get trueSelectedRowIndex(): number | undefined {
    if (getGroupingConfiguration(this).isGrouping) {
      return getGrouper(this).allGroups.some((group) => group.isExpanded)
        ? getGrouper(this).getRowIndex(this.selectedRowId!)
        : undefined;
    } else {
      return this.selectedRowId
        ? this.dataTable.getTrueIndexById(this.selectedRowId)
        : undefined;
    }
  }

  @computed get selectedRow(): any[] | undefined {
    if (!this.selectedRowId) {
      return undefined;
    }
    if (getGroupingConfiguration(this).isGrouping) {
      return getGrouper(this).getRowById(this.selectedRowId!);
    } else {
      return this.selectedRowIndex !== undefined
        ? this.dataTable.getRowByExistingIdx(this.selectedRowIndex)
        : undefined;
    }
  }

  @computed get isValidRowSelection(): boolean {
    return this.selectedRowIndex !== undefined;
  }

  @computed get panelViewActions() {
    const rowStateMayCauseFlicker = getRowStateMayCauseFlicker(this);
    if (rowStateMayCauseFlicker && !this.dataTable.isEmpty) {
      return [];
    }
    return this.actions.filter(
      (action) => action.placement === IActionPlacement.PanelHeader
    );
  }

  @computed get panelMenuActions() {
    const rowStateMayCauseFlicker = getRowStateMayCauseFlicker(this);
    if (rowStateMayCauseFlicker && !this.dataTable.isEmpty) {
      return [];
    }
    return this.actions.filter(
      (action) => action.placement === IActionPlacement.PanelMenu
    );
  }

  @computed get toolbarActions() {
    const rowStateMayCauseFlicker = getRowStateMayCauseFlicker(this);
    if (rowStateMayCauseFlicker && !this.dataTable.isEmpty) {
      return [];
    }
    return this.actions.filter(
      (action) =>
        action.placement === IActionPlacement.Toolbar &&
        action.type !== IActionType.SelectionDialogAction &&
        !getIsDialog(this)
    );
  }

  @computed get dialogActions() {
    return this.actions.filter(
      (action) =>
        action.type === IActionType.SelectionDialogAction || getIsDialog(this)
    );
  }

  @computed get isWorking() {
    return (
      this.lifecycle.isWorking ||
      getRowStates(this).isWorking ||
      getLookupLoader(this).isWorking
    );
  }

  @computed get isAnyBindingAncestorWorking() {
    if (this.isBindingRoot) {
      return false;
    } else {
      return (
        this.bindingParent.isWorking ||
        this.bindingParent.isAnyBindingAncestorWorking
      );
    }
  }

  @computed
  get isBindingRoot() {
    return this.parentBindings.length === 0;
  }

  @computed get isBindingParent() {
    return this.childBindings.length > 0;
  }

  @computed get bindingParent() {
    return this.parentBindings?.[0]?.parentDataView;
  }

  @computed get bindingRoot(): IDataView {
    // TODO: If there ever is multi parent case, remove duplicates in the result
    let root: IDataView = this;
    while (!root.isBindingRoot) {
      root = root.bindingParent!;
    }
    return root;
  }

  @computed
  get parentBindings() {
    const screen = getFormScreen(this);
    return screen.getBindingsByChildId(this.modelInstanceId);
  }

  @computed
  get childBindings() {
    const screen = getFormScreen(this);
    return screen.getBindingsByParentId(this.modelInstanceId);
  }

  @computed get dataSource() {
    return getDataSourceByEntity(this, this.entity)!;
  }

  @computed get bindingParametersFromParent() {
    const parentRow = getParentRow(this);
    if (parentRow) {
      const parent = getBindingParent(this);
      const parentDataTable = getDataTable(parent);

      const bindingToParent = getBindingToParent(this)!;
      const result: { [key: string]: string } = {};
      for (let bp of bindingToParent.bindingPairs) {
        const parentDataSourceField = getDataSourceFieldByName(
          parent,
          bp.parentPropertyId
        )!;
        result[bp.childPropertyId] =
          parentDataTable.getCellValueByDataSourceField(
            parentRow,
            parentDataSourceField
          );
      }
      return result;
    } else {
      return {};
    }
  }

  @action.bound moveSelectedRowUp() {
    if (!this.selectedRowId) {
      return;
    }
    const ordering = getPropertyOrdering(this, this.orderMember);
    if (ordering.ordering === IOrderByDirection.ASC) {
      this.moveSelectedRowDownIndexwise();
    } else {
      this.moveSelectedRowUpIndexwise();
    }
  }

  @action.bound moveSelectedRowDown() {
    if (!this.selectedRowId) {
      return;
    }
    const ordering = getPropertyOrdering(this, this.orderMember);
    if (ordering.ordering === IOrderByDirection.ASC) {
      this.moveSelectedRowUpIndexwise();
    } else {
      this.moveSelectedRowDownIndexwise();
    }
  }

  @action.bound moveSelectedRowUpIndexwise() {
    if (!this.selectedRowId) {
      return;
    }
    const dataTable = getDataTable(this);
    const selectedRow = dataTable.getRowById(this.selectedRowId)!;
    const positionIndex = getDataSourceFieldByName(
      this,
      this.orderMember
    )!.index;
    const selectedRowPosition = selectedRow[positionIndex];
    const nextRow = dataTable.rows.find(
      (row) => row[positionIndex] === selectedRowPosition + 1
    );
    if (!nextRow) {
      return;
    }
    selectedRow[positionIndex] += 1;
    nextRow[positionIndex] -= 1;
    this.dataTable.substituteRecord(selectedRow);
    this.dataTable.substituteRecord(nextRow);
    this.dataTable.updateSortAndFilter({retainPreviousSelection: true});
    this.dataTable.setDirtyValue(
      selectedRow,
      this.orderMember,
      selectedRow[positionIndex]
    );
    this.dataTable.setDirtyValue(
      nextRow,
      this.orderMember,
      nextRow[positionIndex]
    );
  }

  @action.bound moveSelectedRowDownIndexwise() {
    if (!this.selectedRowId) {
      return;
    }
    const dataTable = getDataTable(this);
    const selectedRow = dataTable.getRowById(this.selectedRowId)!;
    const positionIndex = getDataSourceFieldByName(
      this,
      this.orderMember
    )!.index;
    const selectedRowPosition = selectedRow[positionIndex];
    const previous = dataTable.rows.find(
      (row) => row[positionIndex] === selectedRowPosition - 1
    );
    if (!previous) {
      return;
    }
    selectedRow[positionIndex] -= 1;
    previous[positionIndex] += 1;
    this.dataTable.substituteRecord(selectedRow);
    this.dataTable.substituteRecord(previous);
    this.dataTable.updateSortAndFilter({retainPreviousSelection: true});
    this.dataTable.setDirtyValue(
      selectedRow,
      this.orderMember,
      selectedRow[positionIndex]
    );
    this.dataTable.setDirtyValue(
      previous,
      this.orderMember,
      previous[positionIndex]
    );
  }

  @action.bound selectNextRow() {
    const selectedRowId = getSelectedRowId(this);

    let newId = undefined;
    if (selectedRowId) {
      newId = getGroupingConfiguration(this).isGrouping
        ? getGrouper(this).getNextRowId(selectedRowId)
        : getDataTable(this).getNextExistingRowId(selectedRowId);
    }
    if (newId) {
      this.setSelectedRowId(newId);
    }
  }

  @action.bound selectPrevRow() {
    const selectedRowId = getSelectedRowId(this);

    let newId = undefined;
    if (selectedRowId) {
      newId = getGroupingConfiguration(this).isGrouping
        ? getGrouper(this).getPreviousRowId(selectedRowId)
        : getDataTable(this).getPrevExistingRowId(selectedRowId);
    }
    if (newId) {
      this.setSelectedRowId(newId);
    }
  }

  *navigateLookupLink(property: IProperty, row: any[]): any {
    const columnId = property.id;
    const fieldIndex = getDataSourceFieldIndexByName(this, columnId);
    if (fieldIndex === undefined) return;
    const value = row[fieldIndex];
    const menuId = yield selectors.column.getLinkMenuId(property, value);
    let menuItem = menuId && selectors.mainMenu.getItemById(this, menuId);
    if (menuItem) {
      menuItem = {...menuItem, parent: undefined, elements: []};
      menuItem = produce(menuItem, (draft: any) => {
        if (menuItem.attributes.type.startsWith("FormReferenceMenuItem")) {
          draft.attributes.type = "FormReferenceMenuItem";
        }
        draft.attributes.lazyLoading = "false";
      });

      yield onMainMenuItemClick(this)({
        event: undefined,
        item: menuItem,
        idParameter: value,
        isSingleRecordEdit: true,
      });
    }
  }

  @action.bound onFieldChange(
    event: any,
    row: any[],
    property: IProperty,
    newValue: any
  ) {
    if (!property.readOnly) {
      const currentValue = getDataTable(this).getCellValue(row, property);
      if (newValue === currentValue) {
        return;
      }
      getDataTable(this).setFormDirtyValue(row, property.id, newValue);
    }
  }

  @action.bound
  *loadFirstPage(): any {
    if (this.infiniteScrollLoader) yield*this.infiniteScrollLoader!.loadFirstPage();
  }

  @action.bound
  *loadLastPage(): any {
    if (this.infiniteScrollLoader) yield*this.infiniteScrollLoader!.loadLastPage();
  }

  @action.bound selectFirstRow() {
    if (getGroupingConfiguration(this).isGrouping) {
      return;
    }
    const dataTable = getDataTable(this);
    const firstRow = dataTable.getFirstRow();
    if (firstRow) {
      this.setSelectedRowId(dataTable.getRowId(firstRow));
    } else {
      this.setSelectedRowId(undefined);
    }
  }

  @action.bound selectLastRow() {
    if (getGroupingConfiguration(this).isGrouping) {
      return;
    }
    const dataTable = getDataTable(this);
    const lastRow = dataTable.getLastRow();
    if (lastRow) {
      this.setSelectedRowId(dataTable.getRowId(lastRow));
    } else {
      this.setSelectedRowId(undefined);
    }
  }

  reselectOrSelectFirst() {
    const previouslySelectedRowExists =
      this.selectedRowId && this.dataTable.getRowById(this.selectedRowId);
    if (!this.isRootGrid || !previouslySelectedRowExists || !this.selectedRow) {
      this.selectFirstRow();
    }
  }

  @action.bound selectRow(row: any[]) {
    this.setSelectedRowId(this.dataTable.getRowId(row));
  }

  @action.bound
  setSelectedRowId(id: string | undefined): void {
    if (this.selectedRowId === id) {
      return;
    }
    this.selectedRowId = id;
    if (this.isBindingParent) {
      this.childBindings.forEach((binding) =>
        binding.childDataView.dataTable.updateSortAndFilter()
      );
    }
    const self = this;
    if (!this.selectedRowId) {
      return;
    }

    if (getFormScreenLifecycle(this).focusedDataViewId === this.id) {
      runInFlowWithHandler({
        ctx: self,
        action: async () => {
          await onSelectedRowChange(self)(
            getMenuItemId(self),
            getDataStructureEntityId(self),
            self.selectedRowId
          );
        },
      });
    }
  }

  viewStateStack: SavedViewState[] = [];

  @action.bound
  saveViewState(): void {
    this.viewStateStack.push(new SavedViewState(this.selectedRowId));
  }

  @action.bound
  restoreViewState(): void {
    const state = this.viewStateStack.pop();
    if (state && state.selectedRowId) {
      this.setSelectedRowId(state.selectedRowId);
      if (!getSelectedRow(this)) {
        this.selectFirstRow();
      }
      getTablePanelView(this).scrollToCurrentCell();
    }
  }

  get isLazyLoading() {
    return isLazyLoading(this) && this.isRootGrid;
  }

  @action.bound
  async start() {
    this.lifecycle.start();
    const serverSideGrouping = this.isLazyLoading;
    if (serverSideGrouping) {
      this.serverSideGrouper.start();
    }
    getFormScreenLifecycle(this).registerDisposer(() =>
      this.serverSideGrouper.dispose()
    );
    await this.dataTable.start();
    getFormScreenLifecycle(this).registerDisposer(
      reaction(
        () => ({
          selectedRowId: this.selectedRowId,
          rowsCount: getDataTable(this).allRows.length,
        }),
        (reData: { selectedRowId: string | undefined; rowsCount: number }) => {
          if (getFormScreenLifecycle(this).rowSelectedReactionsDisabled(this)) {
            return;
          }
          if (reData.selectedRowId === undefined && reData.rowsCount > 0) {
            this.reselectOrSelectFirst();
          }
        },
        {
          fireImmediately: true,
        }
      )
    );
    await this.dataTable.start();
  }

  @action.bound stop() {
    this.properties.forEach((prop) => prop.stop());
    this.dataTable.stop();
  }

  @computed get tableRows() {
    const groupedColumnIds =
      getGroupingConfiguration(this).orderedGroupingColumnSettings;
    return groupedColumnIds.length === 0
      ? getDataTable(this).rows
      : flattenToTableRows(getGrouper(this).topLevelGroups);
  }

  scrollState = new SimpleScrollState(0, 0);

  @observable contentBounds: BoundingRect | undefined;
  infiniteScrollLoader: IInfiniteScrollLoader | undefined =
    new NullIScrollLoader();

  parent?: any;

  initializeNewScrollLoader() {
    if (this.infiniteScrollLoader) {
      this.infiniteScrollLoader.dispose();
    }
    this.infiniteScrollLoader = this.getScrollLoader();
    getFormScreenLifecycle(this).registerDisposer(
      this.infiniteScrollLoader.start()
    );
  }

  getScrollLoader() {
    const isGroupingOff =
      getGroupingConfiguration(this).orderedGroupingColumnSettings.length === 0;
    const rowsContainer = getDataTable(this).rowsContainer;
    if (rowsContainer instanceof ScrollRowContainer && isGroupingOff) {
      return new InfiniteScrollLoader({
        ctx: this,
        gridDimensions: this.gridDimensions,
        scrollState: this.scrollState,
        rowsContainer: rowsContainer as ScrollRowContainer,
        groupFilter: undefined,
        visibleRowsMonitor: new VisibleRowsMonitor(
          this,
          this.gridDimensions,
          this.scrollState
        ),
      });
    } else {
      return new NullIScrollLoader();
    }
  }

  // Called by client scripts
  focusFormViewControl(name: string) {
    this.formFocusManager.focus(name);
  }

  // Called by client scripts
  showView(viewId: string, focus: boolean) {
    throw new Error("showView method is not yet implemented.");
  }

  // Called by client scripts
  switchToPanel(modelInstanceId: string) {
    throw new Error("switchToPanel method is not yet implemented.");
  }

  onReload() {
    this.dataTable.unlockAddedRowPosition();
  }

  attributes: any;

  async exportToExcel() {
    const visibleColumnIds = getConfigurationManager(this)
      .activeTableConfiguration.columnConfigurations.filter(
        (columnConfig) => columnConfig.isVisible
      )
      .map((columnConfig) => columnConfig.propertyId);
    const fields = getTablePanelView(this)
      .allTableProperties.filter((property) =>
        visibleColumnIds.includes(property.id)
      )
      .map((property) => {
        return {
          Caption: property.name,
          FieldName: property.id,
          LookupId: property.lookupId,
          Format: property.formatterPattern,
          PolymorphRules: property.controlPropertyId
            ? {
              ControlField: property.controlPropertyId,
              Rules: this.getPolymorphicRules(property),
            }
            : undefined,
        };
      });
    const excelMaxRowCount = 1048576;
    const api = getApi(this);
    if (isInfiniteScrollingActive(this)) {
      await api.getExcelFile({
        Entity: this.entity,
        Fields: fields,
        SessionFormIdentifier: getSessionId(this),
        RowIds: [],
        LazyLoadedEntityInput: {
          SessionFormIdentifier: getSessionId(this),
          Filter: getUserFilters({ctx: this}),
          MenuId: getMenuItemId(this),
          DataStructureEntityId: getDataStructureEntityId(this),
          Ordering: getUserOrdering(this),
          RowLimit: excelMaxRowCount,
          RowOffset: 0,
          ColumnNames: getColumnNamesToLoad(this),
          FilterLookups: getUserFilterLookups(this),
        },
      });
    } else {
      await api.getExcelFile({
        Entity: this.entity,
        Fields: fields,
        SessionFormIdentifier: getSessionId(this),
        RowIds: this.dataTable.rows.map((row) => this.dataTable.getRowId(row)),
        LazyLoadedEntityInput: undefined,
      });
    }
  }

  private getPolymorphicRules(property: IProperty) {
    return property.childProperties
      .filter((prop) => prop.controlPropertyValue)
      .reduce((map: { [key: string]: string }, prop: IProperty) => {
        map[prop.controlPropertyValue!] = prop.id;
        return map;
      }, {});
  }
}
