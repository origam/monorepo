import { observable, reaction } from "mobx";
import { IWorkbench } from "model/entities/types/IWorkbench";
import { getWorkbenchLifecycle } from "model/selectors/getWorkbenchLifecycle";
import { IFormScreen } from "model/entities/types/IFormScreen";
import { BreadCrumbsState } from "model/entities/MobileState/BreadCrumbsState";
import { IMobileLayoutState, MenuLayoutState, ScreenLayoutState } from "model/entities/MobileState/MobileLayoutState";
import { getActiveScreen } from "model/selectors/getActiveScreen";

export class MobileState {
  _workbench: IWorkbench | undefined;

  @observable
  layoutState: IMobileLayoutState = new ScreenLayoutState()

  @observable
  activeDataViewId: string | undefined;

  breadCrumbsState = new BreadCrumbsState()

  initialize(workbench: IWorkbench) {
    let workbenchLifecycle = getWorkbenchLifecycle(workbench);
    workbenchLifecycle.mainMenuItemClickHandler.add(
      () => this.layoutState = new ScreenLayoutState()
    );
    this._workbench = workbench;
    this.breadCrumbsState.workbench = workbench;
    this.breadCrumbsState.updateBreadCrumbs();
    this.start();
  }

  lastArgs: any;

  // It is ok for these reactions to run indefinitely because the MobileState is never disposed. Hence, no disposers here.
  start() {
    reaction(
      () => {
        return {
          activeScreen: !!getActiveScreen(this._workbench),
          layoutState: this.layoutState
        };
      },
      (args) => {
        if (!args.activeScreen && args.layoutState instanceof ScreenLayoutState) {
          this.layoutState = new MenuLayoutState();
        }
        else if (
          this.lastArgs && !this.lastArgs.activeScreen && args.activeScreen &&
          args.layoutState instanceof MenuLayoutState
        ) {
          this.layoutState = new ScreenLayoutState();
        }
        this.lastArgs = args;
      },
    );
  }

  async close() {
    this.layoutState = await this.layoutState.close(this._workbench);
  }

  hamburgerClick() {
    this.layoutState = this.layoutState.hamburgerClick();
  }

  onFormClose(formScreen: IFormScreen | undefined) {
    if(!formScreen){
      return;
    }
    this.breadCrumbsState.onFormClose(formScreen);
  }
}

