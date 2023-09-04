/*
Copyright 2005 - 2023 Advantage Solutions, s. r. o.

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

import React from "react";
import S from "./NotificationContainer.module.scss";
import { MobXProviderContext, observer } from "mobx-react";
import { IWorkbench } from "model/entities/types/IWorkbench";
import { getActiveScreen } from "model/selectors/getActiveScreen";
import { Icon } from "@origam/components";
import cx from "classnames";

@observer
export class NotificationContainer extends React.Component<{}> {

  static contextType = MobXProviderContext;

  get workbench(): IWorkbench {
    return this.context.workbench;
  }

  getIcon(iconName: string){
    switch (iconName){
      case "error":
        return <Icon className={cx(S.icon, S.errorIcon)} src="./icons/error-fill.svg"/>
      case "warning":
        return <Icon className={cx(S.icon, S.warningIcon)} src="./icons/warning-fill.svg"/>
      case "info":
        return <Icon className={cx(S.icon, S.infoIcon)} src="./icons/info2.svg"/>
      default:
        return <Icon className={S.icon} src=""/>
    }
  }

  render() {
    const activeScreen = getActiveScreen(this.workbench);
    const formScreen = activeScreen?.content.formScreen;
    if(!formScreen || formScreen.notifications.length === 0){
      return null;
    }
    return (
      <div className={S.root}>
        {formScreen.notifications.map(x =>
          <div className={S.row}>
            <div className={S.iconContainer}>
              {this.getIcon(x.icon)}
            </div>
            <div>{x.text}</div>
          </div>
        )}
      </div>
    );
  }
}



