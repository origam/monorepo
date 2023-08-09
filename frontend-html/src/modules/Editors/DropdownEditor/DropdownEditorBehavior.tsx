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

import _ from "lodash";
import { action, computed, decorate, flow, observable } from "mobx";
import { IDropdownEditorApi } from "./DropdownEditorApi";
import { CancellablePromise, EagerlyLoadedGrid, LazilyLoadedGrid } from "./DropdownEditorCommon";
import { IDropdownEditorData } from "./DropdownEditorData";
import { DropdownEditorLookupListCache } from "./DropdownEditorLookupListCache";
import { DropdownDataTable } from "./DropdownTableModel";
import { IFocusable } from "../../../model/entities/FormFocusManager";
import { compareStrings } from "../../../utils/string";
import { IDriverState } from "modules/Editors/DropdownEditor/Cells/IDriverState";
import { DropdownEditorSetup } from "modules/Editors/DropdownEditor/DropdownEditorSetup";
import { requestFocus } from "utils/focus";

export const  dropdownPageSize = 100;

export interface IDropdownEditorBehavior extends IDriverState{
  scrollToRowIndex: number | undefined;
  willLoadPage: number;
  handleScroll(args: {
    clientHeight: number;
    clientWidth: number;
    scrollHeight: number;
    scrollLeft: number;
    scrollTop: number;
    scrollWidth: number;
  }): void;
}

export interface IBehaviorData {
  api: IDropdownEditorApi,
  data: IDropdownEditorData,
  dataTable: DropdownDataTable,
  setup: () => DropdownEditorSetup,
  cache: DropdownEditorLookupListCache,
  isReadOnly: boolean,
  onDoubleClick?: (event: any) => void,
  onClick?: (event: any) => void,
  subscribeToFocusManager?: (obj: IFocusable) => void,
  onKeyDown?: (event: any) => void,
  autoSort?: boolean,
  onTextOverflowChanged?: (toolTip: string | null | undefined) => void,
}

export class DropdownEditorBehavior implements IDropdownEditorBehavior{

  private api: IDropdownEditorApi;
  private data: IDropdownEditorData;
  private dataTable: DropdownDataTable;
  private setup: () => DropdownEditorSetup;
  private cache: DropdownEditorLookupListCache;
  public isReadOnly: boolean;
  public onDoubleClick?: (event: any) => void;
  public onClick?: (event: any) => void;
  public subscribeToFocusManager?: (obj:IFocusable) => void;
  private onKeyDown?: (event: any) => void;
  private autoSort?: boolean;
  private onTextOverflowChanged?: (toolTip: string | null | undefined) => void;

  constructor(args: IBehaviorData) {
    this.api = args.api;
    this.data = args.data;
    this.dataTable = args.dataTable;
    this.setup = args.setup;
    this.cache = args.cache;
    this.isReadOnly = args.isReadOnly;
    this.onDoubleClick = args.onDoubleClick;
    this.onClick = args.onClick;
    this.subscribeToFocusManager = args.subscribeToFocusManager;
    this.onKeyDown = args.onKeyDown;
    this.autoSort = args.autoSort;
    this.onTextOverflowChanged = args.onTextOverflowChanged;
  }

  @observable isDropped = false;
  @observable isWorking = false;
  @observable userEnteredValue: string | undefined = undefined;
  @observable scrollToRowIndex: number | undefined = undefined;
  dontClearScrollToRow = true;

  @observable cursorRowId = "";

  willLoadPage = 1;
  willLoadNextPage = true;

  @computed get isBodyDisplayed() {
    return this.isDropped && this.dataTable.rowCount > 0;
  }

  @computed get chosenRowId() {
    return this.data.value;
  }

  @computed get inputValue() {
    return this.userEnteredValue !== undefined ? this.userEnteredValue : this.data.text;
  }

  @action.bound
  dropUp() {
    if (this.isDropped) {
      this.ensureRequestCancelled();
      this.userEnteredValue = undefined;
      this.dataTable.clearData();
      this.isDropped = false;
      this.willLoadPage = 1;
      this.willLoadNextPage = true;
      this.scrollToRowIndex = 0;
    }
  }

  @action.bound
  private dropDown() {
    const setup = this.setup();
    if (!this.isDropped) {
      if (setup.dropdownType === EagerlyLoadedGrid) {
        this.dataTable.setFilterPhrase(this.userEnteredValue || "");
      }
      if (
        setup.dropdownType === EagerlyLoadedGrid &&
        setup.cached &&
        this.cache.hasCachedListRows()
      ) {
        this.dataTable.setData(this.cache.getCachedListRows());
      } else {
        this.ensureRequestRunning();
      }
      if (this.chosenRowId !== null && !Array.isArray(this.chosenRowId)) {
        this.cursorRowId = this.chosenRowId;
      }
    }
    this.isDropped = true;
    this.scrollToChosenRowIfPossible();
    this.makeFocused();
  }

  makeFocused() {
    if (this.elmInputElement) {
      requestFocus(this.elmInputElement);
    }
  }

  @action.bound
  private scrollToChosenRowIfPossible() {
    if (this.chosenRowId && !_.isArray(this.chosenRowId)) {
      const index = this.dataTable.getRowIndexById(this.chosenRowId);
      if (index > -1) {
        this.dontClearScrollToRow = true;
        this.scrollToRowIndex = index + 1;
      }
    }
  }

  @action.bound
  private scrollToCursoredRowIfNeeded() {
    const index = this.dataTable.getRowIndexById(this.cursorRowId);
    if (index > -1) {
      this.dontClearScrollToRow = true;
      this.scrollToRowIndex = index + 1;
    }
  }

  @action.bound
  handleInputFocus(event: any) {
    const {target} = event;
    if (target) {
      target.select();
      target.scrollLeft = 0;
    }
  }

  @action.bound
  handleInputBlur(event: any) {
    if (this.userEnteredValue && this.isDropped && !this.isWorking && this.cursorRowId) {
      this.data.chooseNewValue(this.cursorRowId);
    } else if (this.userEnteredValue === "") {
      this.data.chooseNewValue(null);
    }
    this.dropUp();
  }

  @action.bound
  handleInputBtnClick(event: any) {
    if (!this.isDropped) {
      this.dropDown();
    } else {
      this.dropUp();
    }
  }

  @action.bound
  handleInputKeyDown(event: any) {
    switch (event.key) {
      case "Escape":
        this.dropUp();
        this.userEnteredValue = undefined;
        break;
      case "Enter":
        const wasDropped = this.isDropped;
        if (this.isDropped && !this.isWorking) {
          this.data.chooseNewValue(this.cursorRowId === "" ? null : this.cursorRowId);
          this.dropUp();
        }
        if (wasDropped) {
          event.stopPropagation();
          event.preventDefault();
          // Do not pass event to props.onKeyDown
          return;
        }
        break;
      case "Tab":
        if (this.isDropped) {
          if (this.cursorRowId) {
            this.data.chooseNewValue(this.cursorRowId === "" ? null : this.cursorRowId);
          }
        }
        break;
      case "Delete":
        this.userEnteredValue = undefined;
        this.cursorRowId = "";
        this.data.chooseNewValue(null);
        break;
      case "ArrowUp":
        if (this.isDropped) {
          event.preventDefault();
          if (!this.dataTable.getRowById(this.cursorRowId)) {
            this.trySelectFirstRow();
            this.selectChosenRow();
          } else {
            const prevRowId = this.dataTable.getRowIdBeforeId(this.cursorRowId);
            if (prevRowId) {
              this.cursorRowId = prevRowId;
            }
          }
          this.scrollToCursoredRowIfNeeded();
        }
        break;
      case "ArrowDown":
        if (this.isDropped) {
          event.preventDefault();
          if (!this.dataTable.getRowById(this.cursorRowId)) {
            this.trySelectFirstRow();
            this.selectChosenRow();
          } else {
            const nextRowId = this.dataTable.getRowIdAfterId(this.cursorRowId);
            if (nextRowId) {
              this.cursorRowId = nextRowId;
            }
          }
          this.scrollToCursoredRowIfNeeded();
        } else if (event.ctrlKey || event.metaKey) {
          this.dropDown();
          this.trySelectFirstRow();
        }
        break;
    }
    this.onKeyDown && this.onKeyDown(event);
  }

  handleInputChangeDeb = _.debounce(this.handleInputChangeImm, 300);

  @action.bound
  handleInputChange(event: any) {
    this.userEnteredValue = event.target.value;
    this.isDropped = true;

    if (this.setup().dropdownType === EagerlyLoadedGrid) {
      this.dataTable.setFilterPhrase(this.userEnteredValue || "");
      if (this.setup().cached && this.cache.hasCachedListRows()) {
        this.dataTable.setData(this.cache.getCachedListRows());
      } else {
        this.ensureRequestRunning();
      }
      if (!this.dataTable.getRowById(this.cursorRowId) && this.userEnteredValue) {
        this.trySelectFirstRow();
      }
    } else if (this.setup().dropdownType === LazilyLoadedGrid) {
      this.handleInputChangeDeb();
    }
    this.updateTextOverflowState();
  }

  @action.bound
  private handleInputChangeImm() {
    if (this.setup().dropdownType === LazilyLoadedGrid) {
      this.willLoadNextPage = true;
      this.willLoadPage = 1;
      this.dataTable.clearData();
    }
    this.ensureRequestCancelled();
    this.ensureRequestRunning();
  }

  @action.bound
  handleTableCellClicked(event: any, visibleRowIndex: any) {
    const id = this.dataTable.getRowIdentifierByIndex(visibleRowIndex);
    this.data.chooseNewValue(id);
    this.dropUp();
  }

  @action.bound
  handleTriggerContextMenu(event: any) {
    this.dropUp();
  }

  @action.bound
  handleControlMouseDown(event: any) {
    if (this.isDropped) {
      event.stopPropagation();
      event.preventDefault();
    }
  }

  @action.bound
  handleBodyMouseDown(event: any) {
    if (this.isDropped) {
      event.stopPropagation();
      event.preventDefault();
    }
  }

  @action.bound
  handleWindowMouseDown(event: any) {
    if (this.userEnteredValue === "") {
      this.data.chooseNewValue(null);
    }
    if (this.isDropped) {
      this.dropUp();
    }
  }

  @action.bound
  handleScroll(args: {
    clientHeight: number;
    clientWidth: number;
    scrollHeight: number;
    scrollLeft: number;
    scrollTop: number;
    scrollWidth: number;
  }) {
    const setup = this.setup();
    if (setup.dropdownType === LazilyLoadedGrid) {
      if (
        this.willLoadNextPage &&
        !this.isWorking &&
        args.scrollTop > args.scrollHeight - args.clientHeight - 500
      ) {
        this.willLoadPage++;
        this.ensureRequestRunning();
      }
    }
    if (this.dontClearScrollToRow) {
      this.dontClearScrollToRow = false;
    } else {
      this.scrollToRowIndex = undefined;
    }
  }

  @action.bound
  private ensureRequestRunning() {
    if (!this.isWorking) {
      this.runGetLookupList(
        this.setup().dropdownType === EagerlyLoadedGrid ? "" : this.userEnteredValue || ""
      );
    }
  }

  @action.bound
  private ensureRequestCancelled() {
    if (this.runningPromise) this.runningPromise.cancel();
  }

  runningPromise: CancellablePromise<any> | undefined;

  @action.bound
  private trySelectFirstRow() {
    if (this.dataTable.rows.length > 0) {
      this.cursorRowId = this.dataTable.getRowIdentifierByIndex(0);
    } else {
      this.cursorRowId = "";
    }
  }

  @action.bound
  private selectChosenRow() {
    if (
      this.dataTable.rows.length > 0 &&
      this.chosenRowId &&
      !_.isArray(this.chosenRowId) &&
      this.dataTable.getRowById(this.chosenRowId)
    ) {
      this.cursorRowId = this.chosenRowId;
    }
  }

  updateTextOverflowState() {
    if (!this.elmInputElement) {
      return;
    }
    const textOverflow = this.elmInputElement.offsetWidth < this.elmInputElement.scrollWidth
    this.onTextOverflowChanged?.(textOverflow ? this.inputValue : undefined);
  }

  @action.bound
  private runGetLookupList(searchTerm: string) {
    const self = this;
    this.runningPromise = flow(function*() {
      try {
        self.isWorking = true;
        const setup = self.setup();
        const items = yield*self.api.getLookupList(searchTerm);
        if (self.autoSort) {
          items.sort((i1: any[], i2: any[]) => compareLookUpItems(i1[1], i2[1]))
        }
        if (setup.dropdownType === EagerlyLoadedGrid) {
          self.dataTable.setData(items);
          if (setup.cached) {
            self.cache.setCachedListRows(items);
          }
        } else if (setup.dropdownType === LazilyLoadedGrid) {
          if (items.length < dropdownPageSize) {
            self.willLoadNextPage = false;
          }
          self.scrollToRowIndex = undefined;
          self.dataTable.appendData(items);
        }
        if (!self.dataTable.getRowById(self.cursorRowId) && self.userEnteredValue) {
          self.trySelectFirstRow();
        }
        self.scrollToChosenRowIfPossible();
        self.selectChosenRow();
      } finally {
        self.isWorking = false;
        self.runningPromise = undefined;
      }
    })();
  }

  @action.bound
  clearCache() {
    this.cache.clean();
  }

  _refInputDisposer: any;
  refInputElement = (elm: any) => {
    this.elmInputElement = elm;
  };

  elmInputElement: any;
  refDropdownBody = (elm: any) => (this.elmDropdownBody = elm);
  elmDropdownBody: any;
}

export function compareLookUpItems(item1: any, item2: any) {
  if (typeof item1 === 'number' && typeof item2 === 'number') {
    if (item1 > item2) return 1;
    if (item1 < item2) return -1;
    return 0;
  }
  return compareStrings(item1, item2)
}

decorate(DropdownEditorBehavior, {
  isReadOnly: observable,
});

// export const IDropdownEditorBehavior = TypeSymbol<DropdownEditorBehavior>(
//   "IDropdownEditorBehavior"
// );
