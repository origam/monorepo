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
import { createMachine, interpret } from "xstate";
import { EventHandler } from "./events";

export class PeriodicLoader {
  private timeoutHandle: any;

  constructor(private loadFunction: () => Generator, private getChSuccessfulApi: () => EventHandler<{}>) {
    this.interpreter.start();
  }

  interpreter = interpret(
    createMachine(
      {
        predictableActionArguments: true,
        id: "periodicLoader",
        initial: "INITIALIZED",
        states: {
          INITIALIZED: {
            on: {
              START: {
                target: "PERFORMING_LOAD",
              },
            },
          },
          PERFORMING_LOAD: {
            invoke: {
              src: "svcLoadFunction",
              onDone: "WAIT_PERIOD_REST",
              onError: "FAILED",
            },
          },
          FAILED: {
            invoke: {
              src: "svcSuccessfulApiSubs",
            },
            on: {
              SOME_API_SUCCESS: "PERFORMING_LOAD",
            },
            after: {
              60000: "PERFORMING_LOAD",
            },
          },
          WAIT_PERIOD_REST: {
            after: {
              REST_DELAY: "PERFORMING_LOAD",
            },
          },
        },
      },
      {
        actions: {},
        services: {
          svcLoadFunction: (ctx, event) => async () => {
            this.t0 = new Date().valueOf();
            await flow(this.loadFunction)();
            this.t1 = new Date().valueOf();
          },
          svcSuccessfulApiSubs: (ctx, event) => (callback, onReceive) => {
            return this.getChSuccessfulApi().subscribe(() => {
              this.interpreter.send("SOME_API_SUCCESS");
            });
          },
        },
        delays: {
          REST_DELAY: (ctx, event) => {
            return Math.max(0, this.refreshIntervalMs - (this.t1 - this.t0));
          },
        },
      }
    ),
    {devTools: true}
  );

  t0 = 0;
  t1 = 0;
  refreshIntervalMs = 0;

  *start(refreshIntervalMs: number) {
    if (refreshIntervalMs <= 0) return;
    this.refreshIntervalMs = refreshIntervalMs;
    this.t0 = 0;
    this.t1 = 0;
    if (!this.interpreter.state?.matches?.("INITIALIZED")) {
      this.interpreter.stop();
      this.interpreter.start();
    }
    this.interpreter.send("START");
  }

  *stop() {
    this.interpreter.stop();
  }
}
