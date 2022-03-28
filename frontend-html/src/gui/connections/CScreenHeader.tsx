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

import { Icon } from "gui/Components/Icon/Icon";
import { ScreenHeader } from "gui/Components/ScreenHeader/ScreenHeader";
import { ScreenHeaderAction } from "gui/Components/ScreenHeader/ScreenHeaderAction";
import { ScreenHeaderPusher } from "gui/Components/ScreenHeader/ScreenHeaderPusher";
import { MobXProviderContext, observer } from "mobx-react";
import { onFullscreenClick } from "model/actions-ui/ScreenHeader/onFullscreenClick";
import { IWorkbench } from "model/entities/types/IWorkbench";
import { getIsScreenOrAnyDataViewWorking } from "model/selectors/FormScreen/getIsScreenOrAnyDataViewWorking";
import { getOpenedNonDialogScreenItems } from "model/selectors/getOpenedNonDialogScreenItems";
import { getIsCurrentScreenFull } from "model/selectors/Workbench/getIsCurrentScreenFull";
import React from "react";
import { getIsTopmostNonDialogScreen } from "model/selectors/getIsTopmostNonDialogScreen";
import { ScreenheaderDivider } from "gui/Components/ScreenHeader/ScreenHeaderDivider";
import { onWorkflowAbortClick } from "model/actions-ui/ScreenHeader/onWorkflowAbortClick";
import { onWorkflowNextClick } from "model/actions-ui/ScreenHeader/onWorkflowNextClick";

import S from "gui/Components/ScreenHeader/ScreenHeader.module.scss";
import { T } from "utils/translation";
import { ErrorBoundaryEncapsulated } from "gui/Components/Utilities/ErrorBoundary";
import { IOpenedScreen } from "model/entities/types/IOpenedScreen";

@observer
export class CScreenHeader extends React.Component {
  static contextType = MobXProviderContext;

  get workbench(): IWorkbench {
    return this.context.workbench;
  }

  render() {
    const openedScreenItems = getOpenedNonDialogScreenItems(this.workbench);
    const activeScreen = openedScreenItems.find((item) => getIsTopmostNonDialogScreen(item));
    if (!activeScreen) {
      return null;
    }
    return (
      <ErrorBoundaryEncapsulated ctx={activeScreen}>
        <CScreenHeaderInner activeScreen={activeScreen}/>
      </ErrorBoundaryEncapsulated>
    );
  }
}

@observer
class CScreenHeaderInner extends React.Component<{ activeScreen: IOpenedScreen }> {
  render() {
    const {activeScreen} = this.props;
    const {content} = activeScreen;
    const isFullscreen = getIsCurrentScreenFull(activeScreen);
    if (!content) return null;
    const isNextButton = content.formScreen && content.formScreen.showWorkflowNextButton;
    const isCancelButton = content.formScreen && content.formScreen.showWorkflowCancelButton;
    return (
      <>
        <h1 className={"printOnly"}>{activeScreen.formTitle}</h1>
        <ScreenHeader
          isLoading={content.isLoading || getIsScreenOrAnyDataViewWorking(content.formScreen!)}
        >
          <h1>{activeScreen.formTitle}</h1>
          {(isCancelButton || isNextButton) && <ScreenheaderDivider/>}
          {isCancelButton && (
            <button
              className={S.workflowActionBtn}
              onClick={onWorkflowAbortClick(content.formScreen!)}
            >
              {T("Cancel", "button_cancel")}
            </button>
          )}
          {isNextButton && (
            <button
              className={S.workflowActionBtn}
              onClick={onWorkflowNextClick(content.formScreen!)}
            >
              {T("Next", "button_next")}
            </button>
          )}
          <ScreenHeaderPusher/>
          <ScreenHeaderAction onClick={onFullscreenClick(activeScreen)} isActive={isFullscreen}>
            <Icon
              src="./icons/fullscreen.svg"
              tooltip={T("Fullscreen", "fullscreen_button_tool_tip")}
            />
          </ScreenHeaderAction>
        </ScreenHeader>
      </>
    );
  }
}
