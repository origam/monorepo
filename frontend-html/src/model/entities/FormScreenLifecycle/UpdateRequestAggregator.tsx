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

import { createMachine, interpret } from "xstate";
import { IUpdateData } from "../types/IApi";
import _ from "lodash";


interface IUpdateObjectData {
  SessionFormIdentifier: string;
  Entity: string;
  UpdateData: IUpdateData[];
}

interface IExtPromise<T> extends Promise<T> {
  resolve(result?: T): void;

  reject(error?: any): void;
}

function ExtPromise<T>() {
  let resolveFn;
  let rejectFn;
  const promise = new Promise<T>((resolve, reject) => {
    resolveFn = resolve;
    rejectFn = reject;
  });
  (promise as any).resolve = resolveFn;
  (promise as any).reject = rejectFn;
  return promise as IExtPromise<T>;
}

export class UpdateRequestAggregator {
  constructor(
    private api: { updateObject(data: IUpdateObjectData): Promise<any> }
  ) {
    this.interpreter.start();
  }

  registeredRequests: Array<IUpdateObjectData & { promise: IExtPromise<any>; isRunning: boolean }> = [];

  itemJustEnqueued?: IUpdateObjectData & { promise: IExtPromise<any>; isRunning: boolean };

  currentRequest?: IUpdateObjectData & { promise: IExtPromise<any> };

  interpreter = interpret(
    createMachine(
      {
        /** @xstate-layout N4IgpgJg5mDOIC5QFcAOECGAXMB5ARgFZgDGWAdAJIAiAMgKIDEAqgArUCCAKvQPoBK9AIrN6AZR7UA2gAYAuolCoA9rACWWNcoB2ikAA9EATgAsJ8gFYA7CaMyZARgBMRi06sA2ADQgAnogAOCwBmcgCPIyMPAKjgmSsAhwBfJJ80TBwCYjJyNk4eXmp6ACFcZgA5AGFKcoBxFnZuPkERcUlZBSQQFXVNHT1DBABaB2CHcmDojxMHDysrVytggJ9-BFMjSydHCwcLDwsggICUtPRsPCJSCjymwpKyqpr6-VgsC-IMADMcACcACgcVhkAEpGOkLllrrlGgUiqUKtU6h09D0NFpdF1Bk5gqF9nNgsCPNEnKNvH5jNFyEZgqYHCYnKSrM5TiAIZkrjkiq1RIwUV00X1MaBBkNjuQFpEAvMjC5olYLKtEAcrNTIlFTAEnAyHEZWezLtkbrDnowIDowOQ3h8DVCcrcuM9+UpVOj+ljEDMLBN7LNiUyPITyWs5h5yMT1TiLDIDlF9ecOUaYflTebtJbrThyLbOcaU8iHJ0Xb0MQNEFZSRL7PYFnFgtZFRSEKHw8CZCZwh54o4PPGMoboQ7TQ7msJRBJ6NJ5KjXUKywg4psTGMxhYLEYbLjtUrhvSwgFfV2Zg524yrH3Ibnk9xnuQOKxKAIKuVUxbyGptAA3ZQAa0tOaTIc6jvB8n3KF86gQD9vxIbAMQ6Z1ulnUsPQXdtyHpQlowjGR1yMHdtVVDxthkKIZkcIwDycC9E0HE1gPvR9+GfU0wF+X5lF+chUAAG2wL5OIAW2zBMB3tejahApiWMg6DlFgoUEOnAVkPdEVEHcTZSScddiPXZYTHbHcTEDcNdUo5kTDXYFtRSVIQG0ZQIDgPQAOuGcSzUgxEBGBYJimGY5gWaxlh3EYZCcCYDx7dsHBPBlghosSKBoBgPLdYVvIQE9SImGwaXsZc10DHc5m9IFq2CbUsJxJK7TzO54UeJFanSudULXMJqw7LVEh0nd4u9Dcdko6MHACXE6qvblRFENqUPUhdpXIGZCNcRI4tcHcglCFxbFpaZmXPey3PE-NWpUzzMuxfCmzi0JnBrbVnH2TwpsAiSpLAiCLuLDL53mHdV2pNtcIOaUDl7E7RPq69HQY0DBDEXBaAANUneavMGaNVTXX1GWWCIFSB+wVrbOIYx00Z3ro86vsEAApehKkkTHrsQHHLGjRwCfCDdGzWQyAhWiJKPG3FYpMuykiAA */
        predictableActionArguments: true,
        id: "updateObject",
        initial: "IDLE",
        states: {
          IDLE: {
            on: {
              UPDATE_REQUESTED: {
                actions: ["enqueueRequest"],
                target: "UPDATE_DEBOUNCING",
              },
            },
          },
          UPDATE_DEBOUNCING: {
            on: {
              UPDATE_REQUESTED: {
                actions: ["enqueueRequest"],
                target: "UPDATE_DEBOUNCING",
              },
            },
            after: {
              170: "DEQUEUE",
            },
          },
          DEQUEUE: {
            entry: "dequeueToCurrentRequest",
            always: "UPDATING",
          },
          UPDATING: {
            initial: "API_RUNNING",
            states: {
              API_RUNNING: {
                invoke: {
                  src: "apiUpdateObject",
                  onDone: "API_RESOLVED",
                  onError: "API_REJECTED",
                },
              },
              API_RESOLVED: {
                entry: ["resolveCurrentPromise"],
                type: "final",
              },
              API_REJECTED: {
                entry: ["rejectCurrentPromise"],
                type: "final",
              },
            },
            on: {
              UPDATE_REQUESTED: {
                actions: ["enqueueRequest"],
              },
            },
            onDone: [
              {cond: "hasMoreItems", target: "#updateObject.DEQUEUE"},
              {target: "#updateObject.IDLE"},
            ],
            exit: ["resetCurrentRequest"],
          },
        },
      },
      {
        services: {
          apiUpdateObject: (ctx, event) => {
            return this.api.updateObject(this.currentRequest!);
          },
        },
        actions: {
          enqueueRequest: (ctx, event) => {
            const updateObjectItem = this.mergeToQueue(event.payload);
            this.itemJustEnqueued = updateObjectItem;
          },
          dequeueToCurrentRequest: (ctx, event) => {
            const item = this.registeredRequests.shift();
            this.currentRequest = item;
          },
          resolveCurrentPromise: (ctx, event) => {
            this.currentRequest?.promise.resolve(event.data);
          },
          rejectCurrentPromise: (ctx, event) => {
            this.currentRequest?.promise.reject(event.data);
          },
          resetCurrentRequest: (ctx, event) => {
            this.currentRequest = undefined;
          },
        },
        guards: {
          hasMoreItems: (ctx, event) => {
            return this.registeredRequests.length > 0;
          },
        },
      }
    ),
    {devTools: true}
  );

  enqueue(data: IUpdateObjectData): Promise<any | null> {
    this.removeCurrentlyProcessedValues(data);
    if (data.UpdateData.length === 0) {
      return Promise.resolve(null);
    }

    this.interpreter.send({
      type: "UPDATE_REQUESTED",
      payload: data,
    });
    const {itemJustEnqueued} = this;
    this.itemJustEnqueued = undefined;
    return itemJustEnqueued!.promise;
  }

  private removeCurrentlyProcessedValues(newData: IUpdateObjectData) {
    if (
      !this.currentRequest ||
      this.currentRequest.Entity !== newData.Entity
    ) {
      return;
    }
    for (const newUpdateData of Array.from(newData.UpdateData)) {
      for (const updateDataInProgress of this.currentRequest.UpdateData) {
        if (updateDataInProgress.RowId === newUpdateData.RowId) {
          this.removeDuplicateProperties({
            removeFrom: newUpdateData,
            source: updateDataInProgress});
          if (Object.keys(newUpdateData.Values).length === 0) {
            newData.UpdateData.remove(newUpdateData);
          }
        }
      }
    }
  }

  private removeDuplicateProperties(args: {removeFrom: IUpdateData, source: IUpdateData}) {
    for (const processedColumn of Object.keys(args.source.Values)) {
      for (const newColumn of Object.keys(args.removeFrom.Values)) {
        if (processedColumn === newColumn &&
          _.isEqual(args.source.Values[processedColumn], args.removeFrom.Values[newColumn])) {
          delete args.removeFrom.Values[newColumn];
        }
      }
    }
  }

  mergeToQueue(data: IUpdateObjectData) {
    let updateObjectItem = this.registeredRequests.find(
      (item) => item.Entity === data.Entity && !item.isRunning
    );
    if (!updateObjectItem) {
      updateObjectItem = {
        SessionFormIdentifier: data.SessionFormIdentifier,
        Entity: data.Entity,
        UpdateData: data.UpdateData,
        promise: ExtPromise(),
        isRunning: false,
      };
      this.registeredRequests.push(updateObjectItem);
    }

    for (let inputRow of data.UpdateData) {
      let existingRow = updateObjectItem.UpdateData.find(
        (item) => item.RowId === inputRow.RowId
      );
      if (!existingRow) {
        existingRow = {RowId: inputRow.RowId, Values: {}};
        updateObjectItem.UpdateData.push(existingRow);
      }
      existingRow.Values = {...existingRow.Values, ...inputRow.Values};
    }
    return updateObjectItem;
  }
}