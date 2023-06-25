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

import { IPanelViewType } from "./IPanelViewType";
import { IProperty } from "./IProperty";
import { IDataSource } from "./IDataSource";
import { IDataTable } from "./IDataTable";
import { IComponentBinding } from "./IComponentBinding";
import { IDataViewLifecycle } from "../DataViewLifecycle/types/IDataViewLifecycle";
import { ITablePanelView } from "../TablePanelView/types/ITablePanelView";
import { IFormPanelView } from "../FormPanelView/types/IFormPanelView";
import { IAction } from "./IAction";
import { ILookupLoader } from "./ILookupLoader";
import { ServerSideGrouper } from "../ServerSideGrouper";
import { ClientSideGrouper } from "../ClientSideGrouper";
import { IGridDimensions, IScrollState } from "../../../gui/Components/ScreenElements/Table/types";
import { ITableRow } from "../../../gui/Components/ScreenElements/Table/TableRendering/types";
import { BoundingRect } from "react-measure";
import { FormFocusManager } from "../FormFocusManager";
import { DataViewData } from "../../../modules/DataView/DataViewData";
import { DataViewAPI } from "../../../modules/DataView/DataViewAPI";
import { RowCursor } from "../../../modules/DataView/TableCursor";
import { IInfiniteScrollLoader } from "gui/Workbench/ScreenArea/TableView/InfiniteScrollLoader";
import { IAggregation } from "./IAggregation";
import { GridFocusManager } from "../GridFocusManager";
import { ScreenFocusManager } from "model/entities/ScreenFocusManager";

export interface IDataViewData {
  id: string;
  modelInstanceId: string;
  name: string;
  focusManager: ScreenFocusManager;
  modelId: string;
  defaultPanelView: IPanelViewType;
  isHeadless: boolean;
  isMapSupported: boolean;
  disableActionButtons: boolean;
  showAddButton: boolean;
  hideCopyButton: boolean;
  showDeleteButton: boolean;
  showSelectionCheckboxesSetting: boolean;
  type: string;
  attributes: any;

  isGridHeightDynamic: boolean;
  selectionMember: string;
  orderMember: string;
  isDraggingEnabled: boolean;
  entity: string;
  dataMember: string;
  isRootGrid: boolean;
  isRootEntity: boolean;
  isPreloaded: boolean;
  requestDataAfterSelectionChange: boolean;
  confirmSelectionChange: boolean;
  properties: IProperty[];
  actions: IAction[];
  dataTable: IDataTable;
  formViewUI: any;
  activePanelView: IPanelViewType;
  tablePanelView: ITablePanelView;
  formPanelView: IFormPanelView;
  lifecycle: IDataViewLifecycle;
  lookupLoader: ILookupLoader;
  serverSideGrouper: ServerSideGrouper;
  clientSideGrouper: ClientSideGrouper;
  isFirst: boolean;
  newRecordView: string | undefined;

  dataViewRowCursor: RowCursor;
  dataViewApi: DataViewAPI;
  dataViewData: DataViewData;
}

export interface IDataView extends IDataViewData {
  $type_IDataView: 1;

  orderProperty: IProperty | undefined;
  isBindingRoot: boolean;
  isBindingParent: boolean;
  isAnyBindingAncestorWorking: boolean;
  isWorking: boolean;
  parentBindings: IComponentBinding[];
  childBindings: IComponentBinding[];
  bindingRoot: IDataView;
  bindingParent: IDataView | undefined;
  isValidRowSelection: boolean;
  selectedRowId: string | undefined;
  selectedRowIndex: number | undefined;
  trueSelectedRowIndex: number | undefined;
  totalRowCount: number | undefined;
  selectedRow: any[] | undefined;
  dataSource: IDataSource;
  bindingParametersFromParent: { [key: string]: string };
  showSelectionCheckboxes: boolean;
  panelViewActions: IAction[];
  panelMenuActions: IAction[];
  toolbarActions: IAction[];
  dialogActions: IAction[];
  formFocusManager: FormFocusManager;
  gridFocusManager: GridFocusManager;
  firstEnabledDefaultAction: IAction | undefined;
  defaultActions: IAction[];
  aggregationData: IAggregation[];

  isSelected(id: string): boolean;

  hasSelectedRowId(id: string): boolean;

  selectedRowIds: Set<string>;

  addSelectedRowId(id: string): void;

  removeSelectedRowId(id: string): void;

  setSelectedState(rowId: string, newState: boolean): void;

  selectNextRow(): void;

  selectPrevRow(): void;

  onFieldChange(event: any, row: any[], property: IProperty, value: any): void;

  loadFirstPage(): Generator;

  loadLastPage(): Generator;

  selectFirstRow(): void;

  selectLastRow(): void;

  reselectOrSelectFirst(): void;

  selectRow(row: any[]): void;

  setSelectedRowId(id: string | undefined): void;

  focusFormViewControl(fieldId: string): void;

  setRecords(rows: any[][]): Promise<any>;

  appendRecords(rows: any[][]): void;

  substituteRecord(row: any[]): void;

  deleteRowAndSelectNext(row: any[]): void;

  clear(): void;

  navigateLookupLink(property: IProperty, row: any[]): Generator<any>;

  saveViewState(): void;

  restoreViewState(): void;

  start(): void;

  stop(): void;

  scrollState: IScrollState;
  tableRows: ITableRow[];

  onReload(): void;

  gridDimensions: IGridDimensions;
  contentBounds: BoundingRect | undefined;
  infiniteScrollLoader: IInfiniteScrollLoader | undefined;

  parent?: any;

  moveSelectedRowUp(): void;

  moveSelectedRowDown(): void;

  setRowCount(rowCount: number | undefined): void;

  isTableViewActive: () => boolean;
  isFormViewActive: () => boolean;
  activateFormView: ((args?: { saveNewState: boolean }) => Promise<any>) | undefined;
  activateTableView: (() => Promise<any>) | undefined;

  initializeNewScrollLoader(): void;

  exportToExcel(): void;

  isLazyLoading: Boolean;
}

export const isIDataView = (o: any): o is IDataView => o?.$type_IDataView;
