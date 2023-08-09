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

import { IFormScreen } from "model/entities/types/IFormScreen";
import { IDataView } from "model/entities/types/IDataView";
import { getSessionId } from "model/selectors/getSessionId";
import { pluginLibrary } from "plugins/tools/PluginLibrary";
import { WorkflowFinishedPanel } from "gui/Components/WorkflowFinishedPanel/WorkflowFinishedPanel";
import actions from "model/actions-ui-tree";
import { serverValueToPanelSizeRatio } from "model/actions-ui/Splitter/splitterPositionToServerValue";
import { IPanelData } from "gui/Components/Splitter/IPanelData";
import { Splitter } from "gui/Components/Splitter/Splitter";
import SSplitter from "gui/Workbench/ScreenArea/CustomSplitter.module.scss";
import { onSplitterPositionChangeFinished } from "model/actions-ui/Splitter/onSplitterPositionChangeFinished";
import { Label } from "gui/Components/ScreenElements/Label";
import { VBox } from "gui/Components/ScreenElements/VBox";
import { HBox } from "gui/Components/ScreenElements/HBox";
import { getDataViewById } from "model/selectors/DataView/getDataViewById";
import { DataView } from "gui/Components/ScreenElements/DataView";
import { CScreenSectionTabbedView } from "gui/connections/CScreenSectionTabbedView";
import { Box } from "gui/Components/ScreenElements/Box";
import React from "react";
import { findBoxes, findUIChildren } from "xmlInterpreters/xmlUtils";

export function desktopRecursiveBuilder(formScreen: IFormScreen, xso: any) {

  const dataViewMap = new Map<string, IDataView>();

  function run(xso: any) {
    switch (xso.attributes.Type) {
      case "ScreenLevelPlugin":{
        let sessionId = getSessionId(formScreen);
        return pluginLibrary.getComponent(
          {
            name: xso.attributes.Name,
            modelInstanceId: xso.attributes.ModelInstanceId,
            sessionId: sessionId,
            ctx: formScreen
          });
      }
      case "SectionLevelPlugin": {
        let dataView = getDataView(xso);
        let sessionId = getSessionId(formScreen);
        return pluginLibrary.getComponent(
          {
            name: xso.attributes.Name,
            modelInstanceId: xso.attributes.ModelInstanceId,
            sessionId: sessionId,
            ctx: dataView
          });
      }
      case "WorkflowFinishedPanel": {
        return (
          <WorkflowFinishedPanel
            key={xso.$iid}
            isCloseButton={xso.attributes.showWorkflowCloseButton}
            isRepeatButton={xso.attributes.showWorkflowRepeatButton}
            message={xso.attributes.Message}
            onCloseClick={actions.workflow.onCloseClick(formScreen)}
            onRepeatClick={actions.workflow.onRepeatClick(formScreen)}
          />
        );
      }
      case "VSplit":
      case "HSplit": {
        const serverStoredValue = formScreen.getPanelPosition(xso.attributes.ModelInstanceId);
        const panelPositionRatio = serverValueToPanelSizeRatio(serverStoredValue);

        const panels: IPanelData[] = findUIChildren(xso)
          .map((child, idx) => {
              return {
                id: idx,
                positionRatio: idx === 0 ? panelPositionRatio : 1 - panelPositionRatio,
                element: run(child),
              }
            })
          .filter(panel => panel.element !== null);
        return (
          <Splitter
            key={xso.$iid}
            STYLE={SSplitter}
            type={xso.attributes.Type === "HSplit" ? "isHoriz" : "isVert"}
            id={xso.attributes.ModelInstanceId}
            onSizeChangeFinished={(
              panelId1: any,
              panelId2: any,
              panel1SizeRatio: number,
              panel2SizeRatio: number
            ) => {
              if (panelId1 === panels[0].id) {
                onSplitterPositionChangeFinished(formScreen)(
                  xso.attributes.ModelInstanceId,
                  panel1SizeRatio
                );
              }
              if (panelId2 === panels[0].id) {
                onSplitterPositionChangeFinished(formScreen)(
                  xso.attributes.ModelInstanceId,
                  panel2SizeRatio
                );
              }
            }}
            panels={panels}
          />
        );
      }
      case "Label":
        return (
          <Label
            key={xso.$iid}
            height={parseInt(xso.attributes.Height, 10)}
            text={xso.attributes.Name}
          />
        );
      case "VBox":
        return (
          <VBox
            key={xso.$iid}
            width={xso.attributes.Width ? parseInt(xso.attributes.Width, 10) : undefined}
            height={xso.attributes.Height ? parseInt(xso.attributes.Height, 10) : undefined}
          >
            {findUIChildren(xso).map((child) => run(child))}
          </VBox>
        );
      case "HBox":
        return (
          <HBox
            key={xso.$iid}
            width={xso.attributes.Width ? parseInt(xso.attributes.Width, 10) : undefined}
            height={xso.attributes.Height ? parseInt(xso.attributes.Height, 10) : undefined}
          >
            {findUIChildren(xso).map((child) => run(child))}
          </HBox>
        );
      case "TreePanel":
      case "Grid":
        const dataView = getDataViewById(formScreen, xso.attributes.Id);
        if (dataView) {
          dataViewMap.set(xso.attributes.Id, dataView);
        }
        return (
          <DataView
            key={xso.$iid}
            id={xso.attributes.Id}
            modelInstanceId={xso.attributes.ModelInstanceId}
            height={xso.attributes.Height ? parseInt(xso.attributes.Height, 10) : undefined}
            width={xso.attributes.Width ? parseInt(xso.attributes.Width, 10) : undefined}
            isHeadless={xso.attributes.IsHeadless === "true"}
          />
        );
      case "Tab":
        return (
          <CScreenSectionTabbedView
            key={xso.$iid}
            boxes={findBoxes(xso)}
            nextNode={run}
            dataViewMap={dataViewMap}
          />
        );
      case "Box":
        return <Box key={xso.$iid}>{findUIChildren(xso).map((child) => run(child))}</Box>;
      default:
        console.error("Unknown node:", xso);  // eslint-disable-line no-console
        return null;
    }
  }

  function getDataView(xso: any) {
    const dataView = getDataViewById(formScreen, xso.attributes.Id);
    if (dataView) {
      dataViewMap.set(xso.attributes.Id, dataView);
    }
    return dataView;
  }

  return run(xso);
}