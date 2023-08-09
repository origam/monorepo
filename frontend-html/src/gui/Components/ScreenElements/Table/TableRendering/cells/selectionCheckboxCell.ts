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

import {
  context,
  context2d,
  currentDataRow,
  dataView,
  isCheckBoxedTable,
  recordId,
  rowId,
  rowIndex,
  tablePanelView
} from "../renderingValues";
import {
  currentColumnLeft,
  currentColumnLeftVisible,
  currentColumnWidth,
  currentColumnWidthVisible,
  currentRowHeight,
  currentRowTop,
} from "../currentCell";
import {
  applyScrollTranslation,
  checkBoxCellPaddingLeft,
  checkBoxCharacterFontSize,
  drawSelectedRowBorder,
  frontStripWidth,
  topTextOffset
} from "./cellsCommon";
import { CPR } from "utils/canvas";
import { onClick, onMouseMove } from "../onClick";
import { getDataTable } from "model/selectors/DataView/getDataTable";
import { getSelectionMember } from "model/selectors/DataView/getSelectionMember";
import { getDataSourceFieldByName } from "model/selectors/DataSources/getDataSourceFieldByName";
import { getFormScreenLifecycle } from "model/selectors/FormScreen/getFormScreenLifecycle";
import { flow } from "mobx";
import { hasSelectedRowId, setSelectedStateRowId, } from "model/actions-tree/selectionCheckboxes";
import { getSelectedRowId } from "model/selectors/TablePanelView/getSelectedRowId";
import { getTablePanelView } from "model/selectors/TablePanelView/getTablePanelView";

export const selectionCheckBoxColumnWidth = 20;

export function selectionCheckboxCellsWidths() {
  return isCheckBoxedTable() ? [selectionCheckBoxColumnWidth] : [];
}

export function selectionCheckboxCellsDraws() {
  return isCheckBoxedTable()
    ? [
      () => {
        applyScrollTranslation();
        drawSelectionCheckboxBackground();
        const ctx2d = context2d();
        ctx2d.fillStyle = "black";
        const state = dataView().isSelected(rowId());

        const {
          selectionRangeIndex0, 
          selectionRangeIndex1, 
          selectionInProgress, 
          selectionTargetState
        } = tablePanelView();
      
        const isSelectionCandidate = 
          selectionInProgress 
          && selectionRangeIndex0 !== undefined 
          && selectionRangeIndex1 !== undefined 
          && Math.min(selectionRangeIndex0, selectionRangeIndex1) <= rowIndex() 
          && rowIndex() <= Math.max(selectionRangeIndex0, selectionRangeIndex1)

        ctx2d.font = `${(isSelectionCandidate) ? 'bold' : ""} ${CPR() * checkBoxCharacterFontSize}px "Font Awesome 5 Free"`;

        ctx2d.fillText(
          ((!isSelectionCandidate && state) || 
          (isSelectionCandidate && selectionTargetState) ) 
            ? "\uf14a" : "\uf0c8",
          CPR() * (currentColumnLeft() + checkBoxCellPaddingLeft),
          CPR() * (currentRowTop() + topTextOffset)
        );
        registerClickHandler();
      },
    ]
    : [];
}

function registerClickHandler() {
  const ctx = context();
  const row = currentDataRow();
  onClick({
    x: currentColumnLeftVisible(),
    y: currentRowTop(),
    w: currentColumnWidthVisible(),
    h: currentRowHeight(),
    handler(event: any) {
      flow(function*() {
        const tablePanelView = getTablePanelView(ctx);
        const dataTable = getDataTable(ctx);
        const rowId = dataTable.getRowId(row);
        yield* tablePanelView.onSelectionCellClick(event, row, rowId)
      })();
    },
  });
  onMouseMove({
    x: currentColumnLeftVisible(),
    y: currentRowTop(),
    w: currentColumnWidthVisible(),
    h: currentRowHeight(),
    handler(event: any) {
      flow(function*() {
        const tablePanelView = getTablePanelView(ctx);
        const dataTable = getDataTable(ctx);
        const rowId = dataTable.getRowId(row);
        yield* tablePanelView.onSelectionCellMouseMove(event, row, rowId)
      })();  
    }
  })
}

export function selectionCheckboxEmptyCellsWidths() {
  return isCheckBoxedTable() ? [selectionCheckBoxColumnWidth] : [];
}

export function selectionCheckboxEmptyCellsDraws() {
  return isCheckBoxedTable()
    ? [
      () => {
        applyScrollTranslation();
        drawSelectionCheckboxBackground();
      },
    ]
    : [];
}

export function drawSelectionCheckboxBackground() {
  const ctx2d = context2d();
  const selectedRowId = getSelectedRowId(tablePanelView());
  const isRowCursor = selectedRowId && recordId() === selectedRowId;
  ctx2d.fillStyle = getComputedStyle(document.documentElement).getPropertyValue('--background1');
  ctx2d.fillRect(
    CPR() * currentColumnLeft(),
    CPR() * currentRowTop(),
    CPR() * currentColumnWidth(),
    CPR() * currentRowHeight()
  );
  if (isRowCursor) {
    drawSelectedRowBorder(frontStripWidth / 2);
  }
}
