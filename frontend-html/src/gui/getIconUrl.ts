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

import { IMenuItemIcon } from "gui/Workbench/MainMenu/IMenuItemIcon";

export function getIconUrl(iconName: string | IMenuItemIcon, iconPath?: string) {
  switch (iconName) {
    case IMenuItemIcon.Form:
      return "./icons/document.svg";
    case IMenuItemIcon.Workflow:
      return "./icons/settings.svg";
    case IMenuItemIcon.WorkQueue:
      return "./icons/work-queue.svg";
    case IMenuItemIcon.Chat:
      return "./icons/chat.svg";
    case IMenuItemIcon.Folder:
      return "./icons/folder-closed.svg";
    case IMenuItemIcon.DataConstant:
      return "./icons/menu_parameter.png";
    default:
      if (iconPath) {
        return iconPath;
      } else {
        return "./icons/document.svg";
      }
  }
}
