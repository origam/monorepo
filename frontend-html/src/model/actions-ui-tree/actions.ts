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

import { flow } from "mobx";
import { IAction, IActionMode } from "model/entities/types/IAction";
import { getFormScreenLifecycle } from "model/selectors/FormScreen/getFormScreenLifecycle";
import { getGridId } from "model/selectors/DataView/getGridId";
import { getEntity } from "model/selectors/DataView/getEntity";
import { getSelectedRowId } from "model/selectors/TablePanelView/getSelectedRowId";
import { handleError } from "model/actions/handleError";

import selectors from "model/selectors-tree";
import { getDataView } from "model/selectors/DataView/getDataView";
import { crs_fieldBlur_ActionClick } from "model/actions/actionSync";

export default {
  onActionClick(ctx: any) {
    return flow(function*onActionClick(event: any, action: IAction, beforeHandleError?: () => void) {
      try {
        yield*crs_fieldBlur_ActionClick.runGenerator(function*() {
          if (!action.isEnabled) {
            return;
          }
          getDataView(ctx).formFocusManager.stopAutoFocus();
          const lifecycle = getFormScreenLifecycle(ctx);
          const gridId = getGridId(ctx);
          const entity = getEntity(ctx);
          const rowId = getSelectedRowId(ctx);
          switch (action.mode) {
            case IActionMode.Always:
              yield*lifecycle.onExecuteAction(gridId, entity, action, []);
              break;
            case IActionMode.ActiveRecord:
              if (rowId) {
                yield*lifecycle.onExecuteAction(gridId, entity, action, [rowId]);
              }
              break;
            case IActionMode.MultipleCheckboxes:
              const selectedRowIds = selectors.selectionCheckboxes.getSelectedRowIds(ctx);
              yield*lifecycle.onExecuteAction(gridId, entity, action, Array.from(selectedRowIds));
              break;
          }
        });
      } catch (e) {
        beforeHandleError?.();
        yield*handleError(ctx)(e);
        throw e;
      }
    });
  },
};
