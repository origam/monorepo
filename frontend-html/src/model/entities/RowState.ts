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
import { action, computed, createAtom, flow, IAtom, observable } from "mobx";
import { getEntity } from "model/selectors/DataView/getEntity";
import { getApi } from "model/selectors/getApi";
import { getSessionId } from "model/selectors/getSessionId";
import { flashColor2htmlColor } from "utils/flashColorFormat";
import { IRowState, IRowStateColumnItem, IRowStateData, IRowStateItem } from "./types/IRowState";
import { FlowBusyMonitor } from "../../utils/flow";
import { handleError } from "model/actions/handleError";

const maxRowStatesInOneCall = 100;

export enum IIdState {
  LOADING = "LOADING",
  ERROR = "ERROR"
}

export class RowState implements IRowState {
  $type_IRowState: 1 = 1;
  suppressWorkingStatus: boolean = false;

  constructor(data: IRowStateData) {
    Object.assign(this, data);
  }

  monitor: FlowBusyMonitor = new FlowBusyMonitor();

  get isWorking() {
    return this.monitor.isWorkingDelayed;
  }

  @observable firstLoadingPerformed = false;
  @observable temporaryContainersValues?: Map<string, RowStateContainer>;
  @computed get mayCauseFlicker() {
    return !this.firstLoadingPerformed;
  }

  containers: Map<string, RowStateContainer> = new Map<string, RowStateContainer>();

  @observable
  isSomethingLoading = false;

  triggerLoadImm = flow(() => this.triggerLoad(maxRowStatesInOneCall));

  *triggerLoad(rowStatesToLoad?: number): any {
    if (this.isSomethingLoading) {
      return;
    }
    let containersToLoad: Map<string, RowStateContainer> = new Map();
    let reportBusyStatus = true;
    try {
      while (true) {
        try {
          const containers = rowStatesToLoad
            ?  Array.from(this.containers.values()).slice(-rowStatesToLoad)
            : this.containers.values();
          for (let container of containers) {
            if (container.rowId && !container.isValid && !container.processingSate){
              containersToLoad.set(container.rowId, container);
            }
          }
          reportBusyStatus = Array.from(containersToLoad.values()).every(container => !container.suppressWorkingStatus);
          if (reportBusyStatus){
            this.monitor.inFlow++;
          }
          if (containersToLoad.size === 0) {
            break;
          }
          for (let container of containersToLoad.values()) {
            container.processingSate = IIdState.LOADING;
          }
          this.isSomethingLoading = true;
          const api = getApi(this);
          const states = yield api.getRowStates({
            SessionFormIdentifier: getSessionId(this),
            Entity: getEntity(this),
            Ids: Array.from(containersToLoad.values()).map(container => container.rowId)
          });
          this.isSomethingLoading = false;
          this.firstLoadingPerformed = true;
          for (let state of states) {
            this.putValue(state);
            this.containers.get(state.id)!.processingSate = undefined;
          }
        } catch (error) {
          this.isSomethingLoading = false;
          this.firstLoadingPerformed = true;
          for (let container of containersToLoad.values()) {
            container.processingSate = IIdState.ERROR;
          }
          yield* handleError(this)(error);
        } finally {
          if(reportBusyStatus){
            this.monitor.inFlow--;
          }
          containersToLoad.forEach(container => container.suppressWorkingStatus = false);
          containersToLoad.clear();
        }
      }
    } finally {
      // After everything got loaded, here we switch back to provide the values just loaded.
      this.temporaryContainersValues = undefined;
    }
  }
  triggerLoadDebounced = _.debounce(this.triggerLoadImm, 666);

  getValue(rowId: string) {
    if (!this.containers.has(rowId)) {
      this.containers.set(rowId, new RowStateContainer(rowId));
    }
    let container = this.containers.get(rowId)!;
    if (!container.atom) {
      container.suppressWorkingStatus = this.suppressWorkingStatus;
      container.atom = createAtom(
        `RowState atom [${rowId}]`,
        () =>
          requestAnimationFrame(() => {
            this.triggerLoadDebounced();
          }),
        () => {
        }
      )
    }
    container.atom.reportObserved?.();
    if (this.temporaryContainersValues && this.temporaryContainersValues.has(rowId)) {
      return this.temporaryContainersValues.get(rowId)?.rowStateItem;
    } else {
      return this.containers.get(rowId)?.rowStateItem;
    }
  }

  async loadValues(rowIds: string[]) {
    for (const rowId of rowIds) {
      if (!this.containers.has(rowId)) {
        this.containers.set(rowId, new RowStateContainer(rowId));
      }
    }
    await flow(this.triggerLoad.bind(this))();
  }

  hasValue(rowId: string): boolean {
    return this.containers.has(rowId);
  }

  @action.bound
  putValue(state: any) {
    let rowStateItem = new RowStateItem(
      state.id,
      state.allowCreate,
      state.allowDelete,
      flashColor2htmlColor(state.foregroundColor),
      flashColor2htmlColor(state.backgroundColor),
      new Map(
        state.columns.map((column: any) => {
          const rs = new RowStateColumnItem(
            column.name,
            column.dynamicLabel,
            flashColor2htmlColor(column.foregroundColor),
            flashColor2htmlColor(column.backgroundColor),
            column.allowRead,
            column.allowUpdate
          );
          return [column.name, rs];
        })
      ),
      new Set(state.disabledActions),
      state.relations
    );
    if (!this.containers.has(state.id)) {
      this.containers.set(state.id, new RowStateContainer(state.id));
    }
    const container = this.containers.get(state.id);
    container!.rowStateItem = rowStateItem;
    container!.isValid = true;
    this.firstLoadingPerformed = true;
  }

  @action.bound reload() {
   // Store the rest of values to suppress flickering while reloading.
    this.temporaryContainersValues = new Map(this.containers.entries());
    // This actually causes reloading of the values (by views calling getValue(...) )
    for (let rowStateContainer of this.containers.values()) {
      rowStateContainer.atom?.onBecomeUnobservedListeners?.clear();
      rowStateContainer.atom?.onBecomeObservedListeners?.clear();
      rowStateContainer.atom = undefined;
      rowStateContainer.isValid = false;
      rowStateContainer.processingSate = undefined;
    }
  }

  @action.bound clearAll() {
    for (let rowStateContainer of this.containers.values()) {
      rowStateContainer.atom?.onBecomeUnobservedListeners?.clear();
      rowStateContainer.atom?.onBecomeObservedListeners?.clear();
      rowStateContainer.atom = undefined;
      rowStateContainer.isValid = false;
      rowStateContainer.processingSate = undefined;
    }
    this.containers.clear();
    this.firstLoadingPerformed = false;
    this.temporaryContainersValues = undefined;
    // TODO: Wait when something is currently loading.
  }

  parent?: any;
}

export class RowStateItem implements IRowStateItem {
  constructor(
    public id: string,
    public allowCreate: boolean,
    public allowDelete: boolean,
    public foregroundColor: string | undefined,
    public backgroundColor: string | undefined,
    public columns: Map<string, IRowStateColumnItem>,
    public disabledActions: Set<string>,
    public relations: any[]
  ) {
  }
}

export class RowStateColumnItem implements IRowStateColumnItem {
  constructor(
    public name: string,
    public dynamicLabel: string | undefined | null,
    public foregroundColor: string | undefined,
    public backgroundColor: string | undefined,
    public allowRead: boolean,
    public allowUpdate: boolean
  ) {
  }
}

class RowStateContainer {
  public rowId: string;

  @observable
  public rowStateItem: IRowStateItem | undefined;

  @observable
  public isValid: boolean = false;

  public processingSate: IIdState | undefined;
  public suppressWorkingStatus: boolean = false;

  constructor(
    rowId: string,
    public atom?: IAtom
  ) {
    this.rowId = rowId;
  }
}

