import { FormFocusManager } from "model/entities/FormFocusManager";
import { GridFocusManager } from "model/entities/GridFocusManager";
import { getFormScreen } from "model/selectors/FormScreen/getFormScreen";

export class ScreenFocusManager {
  gridManagers: GridFocusManager[] = [];
  formManagers: FormFocusManager[] = [];

  registerGridFocusManager(id: string, manager:GridFocusManager) {
    this.gridManagers.push(manager);
  }
  registerFormFocusManager(id: string, manager:FormFocusManager) {
    this.formManagers.push(manager);
  }

  setFocus() {
    const managerWithOpenEditor =
      this.gridManagers.some(x => x.activeEditor) ||
      this.formManagers.some(x => x.lastFocused);
    if (!managerWithOpenEditor) {
      const formScreen = getFormScreen(this.parent);
      if (formScreen.rootDataViews.length === 1) {
        formScreen.rootDataViews[0].gridFocusManager.focusTableIfNeeded();
      }
    }
  }

  parent: any;
}

