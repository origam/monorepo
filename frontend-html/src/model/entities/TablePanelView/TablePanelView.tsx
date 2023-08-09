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

import { action, computed, flow, observable } from "mobx";
import { onFieldChangeG } from "model/actions-ui/DataView/TableView/onFieldChange";
import { getSelectedRowIndex } from "model/selectors/DataView/getSelectedRowIndex";
import { getCellValue } from "model/selectors/TablePanelView/getCellValue";
import { getSelectedColumnId } from "model/selectors/TablePanelView/getSelectedColumnId";
import { getSelectedColumnIndex } from "model/selectors/TablePanelView/getSelectedColumnIndex";
import { getSelectedRowId } from "model/selectors/TablePanelView/getSelectedRowId";
import { getTableViewProperties } from "model/selectors/TablePanelView/getTableViewProperties";
import { getDataTable } from "model/selectors/DataView/getDataTable";
import { getDataView } from "model/selectors/DataView/getDataView";
import { getDataViewPropertyById } from "model/selectors/DataView/getDataViewPropertyById";
import { IFilterConfiguration } from "model/entities/types/IFilterConfiguration";
import { IOrderingConfiguration } from "model/entities/types/IOrderingConfiguration";
import { IProperty } from "model/entities/types/IProperty";
import {
  ITableCanvas,
  ITablePanelView,
  ITablePanelViewData
} from "model/entities/TablePanelView/types/ITablePanelView";
import { IGroupingConfiguration } from "model/entities/types/IGroupingConfiguration";
import { IAggregationInfo } from "model/entities/types/IAggregationInfo";
import { AggregationType } from "model/entities/types/AggregationType";
import { ICellRectangle } from "model/entities/TablePanelView/types/ICellRectangle";
import { getFormScreen } from "model/selectors/FormScreen/getFormScreen";
import { getTablePanelView } from "model/selectors/TablePanelView/getTablePanelView";
import { flushCurrentRowData } from "model/actions/DataView/TableView/flushCurrentRowData";
import { isReadOnly } from "model/selectors/RowState/isReadOnly";
import { FilterGroupManager } from "model/entities/FilterGroupManager";
import { handleUserInputOnChangingRow } from "../FormScreenLifecycle/questionSaveDataAfterRecordChange";
import { getGroupingConfiguration } from "model/selectors/TablePanelView/getGroupingConfiguration";
import { getGrouper } from "model/selectors/DataView/getGrouper";
import { IConfigurationManager } from "model/entities/TablePanelView/types/IConfigurationManager";
import { isMobileLayoutActive } from "model/selectors/isMobileLayoutActive";
import { ColumnConfigurationModel } from "model/entities/TablePanelView/ColumnConfigurationModel";
import { getOpenedScreen } from "model/selectors/getOpenedScreen";
import { getSelectionMember } from "model/selectors/DataView/getSelectionMember";
import { getDataSourceFieldByName } from "model/selectors/DataSources/getDataSourceFieldByName";
import { getFormScreenLifecycle } from "model/selectors/FormScreen/getFormScreenLifecycle";
import { hasSelectedRowId, setSelectedStateRowId } from "model/actions-tree/selectionCheckboxes";
import { isLazyLoading } from "model/selectors/isLazyLoading";

export class TablePanelView implements ITablePanelView {
  $type_ITablePanelView: 1 = 1;

  constructor(data: ITablePanelViewData) {
    Object.assign(this, data);
    this.columnConfigurationModel.parent = this;
    this.filterConfiguration.parent = this;
    this.filterGroupManager.parent = this;
    this.orderingConfiguration.parent = this;
    this.groupingConfiguration.parent = this;
  }

  columnConfigurationModel: ColumnConfigurationModel = null as any;
  configurationManager: IConfigurationManager = null as any;
  filterConfiguration: IFilterConfiguration = null as any;
  filterGroupManager: FilterGroupManager = null as any;
  orderingConfiguration: IOrderingConfiguration = null as any;
  groupingConfiguration: IGroupingConfiguration = null as any;
  rowHeight: number = null as any;
  firstColumn: IProperty | undefined;
  handleScrolling = true;

  @observable rectangleMap: Map<number, Map<number, ICellRectangle>> = new Map<number,
    Map<number, ICellRectangle>>();

  @observable isEditing: boolean = false;
  @observable fixedColumnCount: number = 0;
  @observable tablePropertyIds: string[] = [];
  @observable hiddenPropertyIds: Map<string, boolean> = new Map();
  @observable columnOrderChangingTargetId: string | undefined;
  @observable columnOrderChangingSourceId: string | undefined;

  @observable _currentTooltipText?: string = undefined;
  @computed get currentTooltipText() {
    return this._currentTooltipText;
  }

  set currentTooltipText(value: string | undefined) {
    this._currentTooltipText = value;
  }

  parent?: any;
  aggregations: AggregationContainer = new AggregationContainer();

  @computed get propertyMap() {
    return new Map(this.allTableProperties.map((x) => [x.id, x] as [string, IProperty]));
  }

  @computed get allTableProperties() {
    return this.tablePropertyIds
      .map((id) => getDataTable(this).getPropertyById(id))
      .filter((prop) => prop) as IProperty[];
  }

  @computed get tableProperties() {
    return this.allTableProperties.filter((prop) => !this.hiddenPropertyIds.get(prop.id));
  }

  @observable selectedColumnId: string | undefined;

  @computed get selectedRowId(): string | undefined {
    return getDataView(this).selectedRowId;
  }

  @computed get selectedColumnIndex(): number | undefined {
    const idx = this.tableProperties.findIndex((prop) => prop.id === this.selectedColumnId);
    return idx > -1 ? idx : undefined;
  }

  @computed get selectedRowIndex(): number | undefined {
    return getDataView(this).selectedRowIndex;
  }

  @computed get selectedProperty(): IProperty | undefined {
    return this.selectedColumnId ? getDataViewPropertyById(this, this.selectedColumnId) : undefined;
  }

  @observable.ref tableCanvas: ITableCanvas | null = null;

  @action.bound
  setTableCanvas(tableCanvas: ITableCanvas | null): void {
    this.tableCanvas = tableCanvas;
  }

  get firstVisibleRowIndex(): number {
    return this.tableCanvas ? this.tableCanvas.firstVisibleRowIndex : 0;
  }

  get lastVisibleRowIndex(): number {
    return this.tableCanvas ? this.tableCanvas.lastVisibleRowIndex : 0;
  }

  getCellValueByIdx(rowIdx: number, columnIdx: number) {
    const property = this.tableProperties[columnIdx]!;
    const row = this.dataTable.getRowByExistingIdx(rowIdx);
    return this.dataTable.getCellValue(row, property);
  }

  getCellTextByIdx(rowIdx: number, columnIdx: number) {
    const property = this.tableProperties[columnIdx]!;
    const row = this.dataTable.getRowByExistingIdx(rowIdx);
    return this.dataTable.getCellText(row, property);
  }

  clearCurrentCellEditData() {
    const row = getDataView(this).selectedRow;
    const propertyId = this.selectedProperty?.id;
    if (row && propertyId) {
      getDataView(this).dataTable.deleteAdditionalCellData(row, propertyId);
    }
  }

  *onCellClick(event: any, row: any[], columnId: string, isControlInteraction: boolean): any {
    const dataView = getDataView(this);
    const rowId = this.dataTable.getRowId(row);

    getTablePanelView(this).setEditing(false);
    yield*flushCurrentRowData(this)();
    const isDirty = getFormScreen(dataView).isDirty;

    if (isDirty && dataView.selectedRowId !== rowId) {
      const shouldProceedToSelectRow = yield handleUserInputOnChangingRow(dataView);
      if (!shouldProceedToSelectRow) {
        return;
      }
    }
    if(isMobileLayoutActive(dataView)){
      yield*this.onCellClickInternalMobile(event, row, columnId, isControlInteraction);
      yield dataView.activateFormView!({saveNewState: false});
    }else{
      yield*this.onCellClickInternal(event, row, columnId, isControlInteraction);
    }
  }

  *onCellClickInternalMobile(event: any, row: any[], columnId: string, isControlInteraction: boolean) {
    const property = this.propertyMap.get(columnId)!;
    if (property.column !== "CheckBox" || !isControlInteraction) {
      this.setEditing(false);
      this.selectCell(this.dataTable.getRowId(row) as string, property.id);
    } else {
      const rowId = this.dataTable.getRowId(row);
      yield*this.selectCellAsync(columnId, rowId);

      if (!isReadOnly(property!, rowId)) {
        yield*onFieldChangeG(this)({
          event: undefined,
          row: row,
          property: property,
          value: !getCellValue(this, row, property),
        });
      }
    }
    if (!getGroupingConfiguration(this).isGrouping) {
      this.scrollToCurrentCell();
    }
  }

  *onCellClickInternal(event: any, row: any[], columnId: string, isControlInteraction: boolean) {
    const property = this.propertyMap.get(columnId)!;
    if (property.column !== "CheckBox" || !isControlInteraction) {
      if (property.isLink && property.column !== "TagInput" && (event.ctrlKey || event.metaKey)) {
        yield*getDataView(this).navigateLookupLink(property, row);
      } else {
        if (this.dataTable.getRowId(row) === this.selectedRowId) {
          this.selectCell(this.dataTable.getRowId(row) as string, property.id);
          this.setEditing(true);
        } else {
          const {isEditing} = this;
          if (isEditing) {
            this.setEditing(false);
          }
          yield*this.selectCellAsync(this.dataTable.getRowId(row) as string, property.id);
          if (isEditing) {
            this.setEditing(true);
          }
        }
      }
    } else {
      const rowId = this.dataTable.getRowId(row);
      yield*this.selectCellAsync(rowId, columnId);

      if (!isReadOnly(property!, rowId)) {
        yield*onFieldChangeG(this)({
          event: undefined,
          row: row,
          property: property,
          value: !getCellValue(this, row, property),
        });
      }
    }
    if (!getGroupingConfiguration(this).isGrouping) {
      this.scrollToCurrentCell();
    }
  }

  private*selectCellAsync(rowId: string, columnId: string,) {
    this.selectedColumnId = columnId;
    const dataView = getDataView(this);
    if (dataView.selectedRowId === rowId) {
      return;
    }
    yield dataView.lifecycle.runRecordChangedReaction(function*() {
      yield dataView.setSelectedRowId(rowId);
    });
  }

  *onNoCellClick() {
    if (this.isEditing) {
      this.setEditing(false);
      yield*flushCurrentRowData(this)();
    }
  }


  lastSelectionRowIdUnderMouse: any = undefined;
  windowMouseMoveDeadPeriod = false;
  @observable shiftPressed = false;
  @observable selectionCellHoveredId: any = undefined;
  @observable selectionTargetState: boolean = true;
  @observable lastSelectedRowId: any = undefined;

  @computed get isMultiSelectEnabled() {
    return !(
      this.groupingConfiguration.isGrouping ||
      isLazyLoading(this) && !!getSelectionMember(this)
    )
  }

  @computed get selectionInProgress() {
    return this.isMultiSelectEnabled && this.shiftPressed;
  }

  @computed get selectionRangeIndex0() {
    if(this.isMultiSelectEnabled && this.lastSelectedRowId !== undefined) {
      const dataTable = getDataTable(this);
      return dataTable.getExistingRowIdxById(this.lastSelectedRowId)
    } else {
      return undefined;
    }
  }

  @computed get selectionRangeIndex1() {
    if(this.isMultiSelectEnabled && this.selectionCellHoveredId !== undefined) {
      const dataTable = getDataTable(this);
      return dataTable.getExistingRowIdxById(this.selectionCellHoveredId);
    } else {
      return undefined;
    }
  }

  windowMouseMoveDeadPeriodTimerHandle: any;
  *onSelectionCellMouseMove(event: any, row: any[], rowId: any) {
    if(this.lastSelectionRowIdUnderMouse !== rowId) {
      if(this.lastSelectionRowIdUnderMouse) {
        yield* this.onSelectionCellMouseOut(event, this.lastSelectionRowIdUnderMouse)
      }
      yield* this.onSelectionCellMouseIn(event, rowId)
    }
    this.lastSelectionRowIdUnderMouse = rowId;
    this.windowMouseMoveDeadPeriod = true;
    clearTimeout(this.windowMouseMoveDeadPeriodTimerHandle);
    this.windowMouseMoveDeadPeriodTimerHandle = setTimeout(() => {
      this.windowMouseMoveDeadPeriod = false;
    }, 0)
  }

  *onSelectionCellClick(event: any, row: any[], rowId: any) {
    const dataTable = getDataTable(this);
    const rowsToSelect: {id: any, row: any[]}[] = [];
    if(this.isMultiSelectEnabled && event.shiftKey && this.lastSelectedRowId !== undefined) {
        const rowRangeStart = dataTable.getExistingRowIdxById(this.lastSelectedRowId);
        const rowRangeEnd = dataTable.getExistingRowIdxById(rowId);
        if(rowRangeStart !== undefined && rowRangeEnd !== undefined) {
          for(
            let i = Math.min(rowRangeStart, rowRangeEnd); 
            i <= Math.max(rowRangeStart, rowRangeEnd); 
            i++
          ) {
            const rowItem = dataTable.getRowByExistingIdx(i)
            const rowItemId = dataTable.getRowId(rowItem);
            rowsToSelect.push({row: rowItem, id: rowItemId})
          }
        } else {
          rowsToSelect.push({row, id: rowId})
        }
    } else {
      rowsToSelect.push({row, id: rowId});
    }

    if(rowsToSelect.length > 0) {
      this.lastSelectedRowId = rowsToSelect.slice(-1)[0].id;
    }

    const newSelectionState = rowsToSelect.length === 1 
      ? !this.getIsRowSelected(rowId, row) 
      : this.selectionTargetState;

    if (rowsToSelect.length === 1) {
      this.selectionTargetState = newSelectionState;
    }
    
    const selectionMember = getSelectionMember(this);
    if (!!selectionMember) {
      const dataSourceField = getDataSourceFieldByName(this, selectionMember);
      if (dataSourceField) {
        for (let rowToSelect of rowsToSelect) {
          dataTable.setDirtyValue(rowToSelect.row, selectionMember, newSelectionState);
        }
        yield*getFormScreenLifecycle(this).onFlushData();
        for (let rowToSelect of rowsToSelect) {
          const updatedRow = dataTable.getRowById(rowToSelect.id)!;
          const updatedSelectionState = dataTable.getCellValueByDataSourceField(updatedRow, dataSourceField);
          yield*setSelectedStateRowId(this)(rowToSelect.id, updatedSelectionState);
        }
      }
    } else {
      for (let rowToSelect of rowsToSelect) {
        yield*setSelectedStateRowId(this)(rowToSelect.id, newSelectionState);
      }
    }
  }


  getIsRowSelected(rowId: any, row: any[]) {
    const dataTable = getDataTable(this);
    const selectionMember = getSelectionMember(this);
    if(!!selectionMember) {
      const dataSourceField = getDataSourceFieldByName(this, selectionMember);  
      return !!dataSourceField && dataTable.getCellValueByDataSourceField(row, dataSourceField);
    } else {
      return hasSelectedRowId(this, rowId)
    }
  }


  *onWindowMouseMove(event: any) {
    if(!event.shiftKey) {
      this.shiftPressed = false;
    }
    if(!this.windowMouseMoveDeadPeriod) {
      if(this.lastSelectionRowIdUnderMouse !== undefined) {
        yield* this.onSelectionCellMouseOut(event, this.lastSelectionRowIdUnderMouse)
        this.lastSelectionRowIdUnderMouse = undefined;
      }
    }
  }
  
  *onSelectionCellMouseIn(event: any, rowId: any) {
    this.selectionCellHoveredId = rowId;
  }

  *onSelectionCellMouseOut(event: any, rowId: any) {
    this.selectionCellHoveredId = undefined;
  }

  *onWindowKeyDown(event: any) {
    if(event.key === 'Shift') {
      this.shiftPressed = true;
    }
  }

  *onWindowKeyUp(event: any) {
    if(event.key === 'Shift') {
      this.shiftPressed = false;
    }
  }


  @action.bound
  handleTableScroll(event: any, scrollTop: number, scrollLeft: number) {
    if (!this.handleScrolling) {
      this.handleScrolling = true;
      return;
    }

    const _this = this;
    flow(function*() {
      if (_this.isEditing) {
        if(!_this.isScrollByKeyboard) {
          _this.setEditing(false);
        }
        yield*flushCurrentRowData(_this)();
      }
    })();
  }

  hScrollDead: any;
  isScrollByKeyboard = false;
  @action.bound handleEditorKeyDown(event: any) {
    if (event.key === "Tab" || event.key === "Enter") {
      clearTimeout(this.hScrollDead);
      this.isScrollByKeyboard = true;
      this.hScrollDead = setTimeout(() => {
        this.isScrollByKeyboard = false;
      });
    }
  }

  dontHandleNextScroll() {
    this.handleScrolling = false;
  }

  *onOutsideTableClick() {
    if (this.isEditing) {
      this.setEditing(false);
      yield*flushCurrentRowData(this)();
    }
  }

  @action.bound selectCell(rowId: string | undefined, columnId: string | undefined) {
    this.selectedColumnId = columnId;
    getDataView(this).setSelectedRowId(rowId);
  }

  isFirstColumnSelected(): boolean {
    const properties = getTableViewProperties(this);
    const selPropId = getSelectedColumnId(this);
    if (!selPropId) {
      return false;
    }
    const idx = properties.findIndex((prop) => prop.id === selPropId);
    return idx === 0;
  }

  isLastColumnSelected(): boolean {
    const properties = getTableViewProperties(this);
    const selPropId = getSelectedColumnId(this);
    if (!selPropId) {
      return false;
    }
    const idx = properties.findIndex((prop) => prop.id === selPropId);
    return idx === properties.length - 1;
  }

  @action.bound
  selectNextColumn(nextRowWhenEnd?: boolean): void {
    const properties = getTableViewProperties(this);
    const selPropId = getSelectedColumnId(this);
    if (selPropId) {
      const idx = properties.findIndex((prop) => prop.id === selPropId);
      if (idx < properties.length - 1) {
        const newProp = properties[idx + 1];
        this.setSelectedColumnId(newProp.id);
      } else if (nextRowWhenEnd && properties.length > 1) {
        const rowId = getSelectedRowId(this);
        getDataView(this).selectNextRow();
        if (rowId !== getSelectedRowId(this)) {
          this.selectFirstColumn();
        }
      }
    }
  }

  @action.bound
  selectPrevColumn(prevRowWhenStart?: boolean): void {
    const properties = getTableViewProperties(this);
    const selPropId = getSelectedColumnId(this);
    if (selPropId) {
      const idx = properties.findIndex((prop) => prop.id === selPropId);
      if (idx > 0) {
        const newProp = properties[idx - 1];
        this.setSelectedColumnId(newProp.id);
      } else if (prevRowWhenStart && properties.length > 1) {
        const rowId = getSelectedRowId(this);
        getDataView(this).selectPrevRow();
        if (rowId !== getSelectedRowId(this)) {
          this.selectLastColumn();
        }
      }
    }
  }

  @action.bound selectFirstColumn(): void {
    const properties = getTableViewProperties(this);
    const newProp = properties[0];
    this.setSelectedColumnId(newProp.id);
  }

  @action.bound selectLastColumn(): void {
    const properties = getTableViewProperties(this);
    const newProp = properties[properties.length - 1];
    this.setSelectedColumnId(newProp.id);
  }

  @action.bound
  setSelectedColumnId(id: string | undefined): void {
    this.selectedColumnId = id;
  }

  @action.bound
  setEditing(state: boolean): void {
    this.isEditing = state;
  }

  @action.bound
  moveColumn(idToMove: string, idToMoveBehind: string): void {
    const idx1 = this.tablePropertyIds.findIndex((id) => id === idToMove);
    const idx2 = this.tablePropertyIds.findIndex((id) => id === idToMoveBehind);
    this.tablePropertyIds.splice(idx1,1);
    this.tablePropertyIds.splice(idx2,0, idToMove);
  }

  @action.bound
  setColumnOrderChangeAttendants(idSource: string | undefined, idTarget: string | undefined): void {
    this.columnOrderChangingTargetId = idTarget;
    this.columnOrderChangingSourceId = idSource;
  }

  subId = 0;
  onScrollToCurrentCellHandlers: Map<number,
    (rowIdx: number, columnIdx: number) => void> = new Map();

  subOnScrollToCellShortest(fn: (rowIdx: number, columnIdx: number) => void): () => void {
    const myId = this.subId++;
    this.onScrollToCurrentCellHandlers.set(myId, fn);
    return () => this.onScrollToCurrentCellHandlers.delete(myId);
  }

  @action.bound scrollToCurrentRow() {
    const rowIdx = getSelectedRowIndex(this);
    if (rowIdx !== undefined) {
      this.triggerOnScrollToCellShortest(rowIdx, 0);
    }
  }

  @action.bound scrollToCurrentCell() {
    const rowIdx = getSelectedRowIndex(this);
    const columnIdx = getSelectedColumnIndex(this);
    if (rowIdx !== undefined && columnIdx !== undefined) {
      this.triggerOnScrollToCellShortest(rowIdx, columnIdx);
    }
  }

  @action.bound triggerOnScrollToCellShortest(rowIdx: number, columnIdx: number) {
    for (let h of this.onScrollToCurrentCellHandlers.values()) h(rowIdx, columnIdx);
  }

  onFocusTableHandlers: Map<number, () => void> = new Map();

  subOnFocusTable(fn: () => void): () => void {
    const myId = this.subId++;
    this.onFocusTableHandlers.set(myId, fn);
    return () => this.onFocusTableHandlers.delete(myId);
  }

  @action.bound
  setPropertyHidden(propertyId: string, state: boolean): void {
    if (state) {
      this.hiddenPropertyIds.set(propertyId, true);
    } else {
      this.hiddenPropertyIds.delete(propertyId);
    }
  }

  @action.bound triggerOnFocusTable() {
    for (let h of this.onFocusTableHandlers.values()) h();
  }

  @computed get dataTable() {
    return getDataTable(this);
  }

  getCellRectangle(rowIndex: number, columnIndex: number): ICellRectangle | undefined {
    const groupingConfig = getGroupingConfiguration(this);
    let cellOffset = {row: 0, column: 0};
    if (groupingConfig.isGrouping) {
      const rowId = getDataView(this).selectedRowId;
      cellOffset = getGrouper(this).getCellOffset(rowId!);
    }
    if (!this.rectangleMap.has(rowIndex + cellOffset.row)) {
      return {
        columnLeft: 0,
        columnWidth: 0,
        rowTop: 0,
        rowHeight: 0,
      };
    }
    const rectangle = this.rectangleMap.get(rowIndex + cellOffset.row)!.get(columnIndex + cellOffset.column)!;
    if (!rectangle) {
      return undefined;
    }
    const openedScreen = getOpenedScreen(this);

    return {
      columnLeft: rectangle.columnLeft + openedScreen.positionOffset.leftOffset,
      columnWidth: rectangle.columnWidth,
      rowTop: rectangle.rowTop + openedScreen.positionOffset.topOffset,
      rowHeight: rectangle.rowHeight
    };
  }

  setCellRectangle(rowId: number, columnId: number, rectangle: ICellRectangle) {
    if (!this.rectangleMap.has(rowId)) {
      this.rectangleMap.set(rowId, new Map<number, ICellRectangle>());
    }
    this.rectangleMap.get(rowId)!.set(columnId, rectangle);
  }
}

export class AggregationContainer {
  @observable aggregationTypes: Map<string, AggregationType | undefined> = new Map<string,
    AggregationType | undefined>();

  getType(columnId: string) {
    return this.aggregationTypes.get(columnId);
  }

  setType(columnId: string, aggregationType: AggregationType | undefined) {
    if (!aggregationType && this.aggregationTypes.has(columnId)) {
      this.aggregationTypes.delete(columnId);
      return;
    }
    if (!aggregationType && !this.aggregationTypes.has(columnId)) return;

    this.aggregationTypes.set(columnId, aggregationType);
  }

  @computed get aggregationList(): IAggregationInfo[] {
    // @ts-ignore
    return Array.from(this.aggregationTypes.entries())
      .filter((entry) => entry[1])
      .map((entry) => {
        return {
          ColumnName: entry[0],
          AggregationType: entry[1],
        };
      });
  }
}
