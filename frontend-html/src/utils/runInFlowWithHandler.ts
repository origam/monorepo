import {flow} from "mobx";
import {handleError} from "model/actions/handleError";

export function wrapInFlowWithHandler(args: { ctx: any; action: (() => Promise<any>) | (() => void) }) {
  return flow(function* runWithHandler() {
    try {
      yield args.action();
    } catch (e) {
      yield* handleError(args.ctx)(e);
      throw e;
    }
  });
}

export function runInFlowWithHandler(args:{ctx: any, action: (()=> Promise<any>) | (()=> void) }) {
  return wrapInFlowWithHandler(args)();
}

export function runGeneratorInFlowWithHandler(args:{ctx: any, generator: Generator}) {
  return flow(function* runWithHandler() {
    try {
      return yield*args.generator;
    } catch (e) {
      yield* handleError(args.ctx)(e);
      throw e;
    }
  })();
}