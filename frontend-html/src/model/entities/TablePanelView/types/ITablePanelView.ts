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

import { IFilterConfiguration } from "../../types/IFilterConfiguration";
import { IProperty } from "../../types/IProperty";
import { IOrderingConfiguration } from "model/entities/types/IOrderingConfiguration";
import { IGroupingConfiguration } from "model/entities/types/IGroupingConfiguration";
import { AggregationContainer } from "../TablePanelView";
import { ICellRectangle } from "./ICellRectangle";

import { FilterGroupManager } from "model/entities/FilterGroupManager";
import { IConfigurationManager } from "model/entities/TablePanelView/types/IConfigurationManager";
import { ColumnConfigurationModel } from "model/entities/TablePanelView/ColumnConfigurationModel";

export interface ITablePanelViewData {
  tablePropertyIds: string[];
  columnConfigurationModel: ColumnConfigurationModel;
  filterConfiguration: IFilterConfiguration;
  filterGroupManager: FilterGroupManager;
  orderingConfiguration: IOrderingConfiguration;
  groupingConfiguration: IGroupingConfiguration;
  rowHeight: number;
}

export interface ITableCanvas {
  firstVisibleRowIndex: number;
  lastVisibleRowIndex: number;
}

export interface ITablePanelView extends ITablePanelViewData {
  firstColumn: IProperty | undefined;
  $type_ITablePanelView: 1;
  selectedColumnId: string | undefined;
  selectedColumnIndex: number | undefined;
  selectedProperty: IProperty | undefined;
  selectedRowIndex: number | undefined;
  isEditing: boolean;
  fixedColumnCount: number;
  configurationManager: IConfigurationManager;

  tableCanvas: ITableCanvas | null;

  setTableCanvas(tableCanvas: ITableCanvas | null): void;

  firstVisibleRowIndex: number;
  lastVisibleRowIndex: number;

  tableProperties: IProperty[];
  allTableProperties: IProperty[];
  propertyMap: Map<string, IProperty>;

  hiddenPropertyIds: Map<string, boolean>;
  aggregations: AggregationContainer;

  currentTooltipText: string | undefined;

  getCellValueByIdx(rowIdx: number, columnIdx: number): any;

  getCellTextByIdx(rowIdx: number, columnIdx: number): any;

  onCellClick(event: any, row: any[], columnId: string, isControlInteraction: boolean): Generator;

  onNoCellClick(): Generator;

  onSelectionCellMouseMove(event: any, row: any[], rowId: any): Generator;

  onSelectionCellClick(event: any, row: any[], rowId: any): Generator;

  selectionRangeIndex0: number | undefined;
  selectionRangeIndex1: number | undefined;
  shiftPressed: boolean;
  selectionTargetState: boolean;
  selectionInProgress: boolean;

  onWindowMouseMove(event: any): Generator;

  onWindowKeyDown(event: any): Generator;

  onWindowKeyUp(event: any): Generator;

  selectionCellHoveredId: any;

  onOutsideTableClick(): Generator;

  dontHandleNextScroll(): void;

  setEditing(state: boolean): void;

  selectNextColumn(nextRowWhenEnd?: boolean): void;

  selectPrevColumn(prevRowWhenStart?: boolean): void;

  setSelectedColumnId(id: string | undefined): void;

  moveColumn(idToMove: string, idToMoveBehind: string): void;

  columnOrderChangingTargetId: string | undefined;
  columnOrderChangingSourceId: string | undefined;

  setColumnOrderChangeAttendants(idSource: string | undefined, idTarget: string | undefined): void;

  subOnScrollToCellShortest(fn: (rowIdx: number, columnIdx: number) => void): () => void;

  subOnFocusTable(fn: () => void): () => void;

  scrollToCurrentCell(): void;

  scrollToCurrentRow(): void;

  triggerOnFocusTable(): void;

  triggerOnScrollToCellShortest(rowIdx: number, columnIdx: number): void;

  setPropertyHidden(propertyId: string, state: boolean): void;

  getCellRectangle(rowIndex: number, columnIndex: number): ICellRectangle | undefined;

  setCellRectangle(rowId: number, columnId: number, rectangle: ICellRectangle): void;

  handleEditorKeyDown(event: any): void;

  clearCurrentCellEditData(): void;

  parent?: any;

  isLastColumnSelected(): boolean;

  isFirstColumnSelected(): boolean;
}
