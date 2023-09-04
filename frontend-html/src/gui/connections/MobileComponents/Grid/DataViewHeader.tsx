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

import { Dropdowner } from "gui/Components/Dropdowner/Dropdowner";
import { CtxDataViewHeaderExtension, DataViewHeaderExtension, } from "gui/Components/ScreenElements/DataView";
import { DataViewHeaderAction } from "gui/Components/DataViewHeader/DataViewHeaderAction";
import { DataViewHeaderGroup } from "gui/Components/DataViewHeader/DataViewHeaderGroup";
import { Dropdown } from "gui/Components/Dropdown/Dropdown";
import { DropdownItem } from "gui/Components/Dropdown/DropdownItem";
import { Icon } from "@origam/components";
import { FilterDropDown } from "gui/connections/FilterDropDown";
import { MobXProviderContext, Observer, observer } from "mobx-react";
import uiActions from "model/actions-ui-tree";
import { getIsRowMovingDisabled } from "model/actions-ui/DataView/getIsRowMovingDisabled";
import { onCopyRowClick } from "model/actions-ui/DataView/onCopyRowClick";
import { onCreateRowClick } from "model/actions-ui/DataView/onCreateRowClick";
import { onDeleteRowClick } from "model/actions-ui/DataView/onDeleteRowClick";
import { onExportToExcelClick } from "model/actions-ui/DataView/onExportToExcelClick";
import { onMoveRowDownClick } from "model/actions-ui/DataView/onMoveRowDownClick";
import { onMoveRowUpClick } from "model/actions-ui/DataView/onMoveRowUpClick";
import { onNextRowClick } from "model/actions-ui/DataView/onNextRowClick";
import { onPrevRowClick } from "model/actions-ui/DataView/onPrevRowClick";
import { onRecordInfoClick } from "model/actions-ui/RecordInfo/onRecordInfoClick";
import { IAction, IActionMode } from "model/entities/types/IAction";
import { getIsEnabledAction } from "model/selectors/Actions/getIsEnabledAction";
import { getIsAddButtonVisible } from "model/selectors/DataView/getIsAddButtonVisible";
import { getIsCopyButtonVisible } from "model/selectors/DataView/getIsCopyButtonVisible";
import { getIsDelButtonVisible } from "model/selectors/DataView/getIsDelButtonVisible";
import { getIsMoveRowMenuVisible } from "model/selectors/DataView/getIsMoveRowMenuVisible";
import { getSelectedRow } from "model/selectors/DataView/getSelectedRow";
import { getOpenedScreen } from "model/selectors/getOpenedScreen";
import { getIsFilterControlsDisplayed } from "model/selectors/TablePanelView/getIsFilterControlsDisplayed";
import React, { useContext } from "react";
import Measure from "react-measure";
import { T } from "utils/translation";
import { getConfigurationManager } from "model/selectors/TablePanelView/getConfigurationManager";
import { action, computed } from "mobx";
import { getPanelMenuActions } from "model/selectors/DataView/getPanelMenuActions";
import { DropdownDivider } from "gui/Components/Dropdown/DropdownDivider";
import { getAreCrudButtonsEnabled } from "model/selectors/DataView/getAreCrudButtonsEnabled";
import {  renderRowCount} from "gui/connections/CDataViewHeader";
import { DataViewHeader } from "gui/Components/DataViewHeader/DataViewHeader";
import "gui/connections/MobileComponents/Grid/DataViewHeader.module.scss"
import { getMobileState } from "model/selectors/getMobileState";
import { EditLayoutState, ScreenLayoutState } from "model/entities/MobileState/MobileLayoutState";
import { FilterEditor } from "gui/connections/MobileComponents/Grid/FilterEditor";
import { ColumnConfiguration } from "gui/connections/MobileComponents/Grid/ColumnConfiguration";
import { getColumnConfigurationModel } from "model/selectors/getColumnConfigurationModel";
import { saveColumnConfigurationsAsync } from "model/actions/DataView/TableView/saveColumnConfigurations";
import { getRecordInfo } from "model/selectors/RecordInfo/getRecordInfo";
import { RecordInfo } from "gui/connections/MobileComponents/Grid/RecordInfo";
import {
  isAddRecordShortcut,
  isDeleteRecordShortcut,
  isDuplicateRecordShortcut,
  isFilterRecordShortcut
} from "utils/keyShortcuts";

@observer
export class DataViewHeaderInner extends React.Component<{
  isVisible: boolean;
  extension: DataViewHeaderExtension;
}> {
  static contextType = MobXProviderContext;

  get dataView() {
    return this.context.dataView;
  }

  state = {
    hiddenActionIds: new Set<string>(),
  };

  shouldBeShown(action: IAction) {
    return getIsEnabledAction(action) || action.mode !== IActionMode.ActiveRecord;
  }

  @computed
  get relevantMenuActions() {
    return this.allMenuActions
      .filter((action) => !action.groupId)
      .filter((action) => this.shouldBeShown(action));
  }

  renderMenuActions(args: { setMenuDropped(state: boolean): void }) {
    return this.relevantMenuActions.map((action) => {
      return (
        <DropdownItem
          key={action.id}
          onClick={(event) => {
            args.setMenuDropped(false);
            uiActions.actions.onActionClick(action)(event, action);
          }}
        >
          {action.caption}
        </DropdownItem>
      );
    });
  }

  get mobileState(){
    return getMobileState(this.dataView);
  }

  @computed
  get isBarVisible() {
    return this.props.isVisible;
  }

  @computed
  get allMenuActions() {
    return getPanelMenuActions(this.dataView);
  }

  @action
  onFilterButtonClick(){
    this.mobileState.layoutState =  new EditLayoutState(
      <FilterEditor dataView={this.dataView}/>,
      T("Filter", "filter_tool_tip"),
      new ScreenLayoutState(),
      true,
    );
  }

  @action
  onColumnConfigurationClick(){
    const configurationModel = getColumnConfigurationModel(this.dataView);
    configurationModel.reset();
    this.mobileState.layoutState =  new EditLayoutState(
      <ColumnConfiguration dataView={this.dataView}/>,
      !configurationModel.columnsConfiguration.name
        ?  T("Default View", "default_grid_view_view")
        : configurationModel.columnsConfiguration.name,
      new ScreenLayoutState(),
      false,
    );
  }

  async onRecordInfoClick(dataView: any){
    await onRecordInfoClick(dataView)(null);
    let previousLayout = this.mobileState.layoutState;
    this.mobileState.layoutState = new EditLayoutState(
      <RecordInfo recordInfo={getRecordInfo(dataView)}/>,
      T("Record Info", "infopanel_title"),
      previousLayout
    );
  }

  render() {
    const {dataView} = this;
    const isFilterSettingsVisible = getIsFilterControlsDisplayed(dataView);
    const onExportToExcelClickEvt = onExportToExcelClick(dataView);
    const onDeleteRowClickEvt = onDeleteRowClick(dataView);
    const onCreateRowClickEvt = onCreateRowClick(dataView, true);
    const onMoveRowUpClickEvt = onMoveRowUpClick(dataView);
    const isRowMovingDisabled = getIsRowMovingDisabled(dataView);
    const onMoveRowDownClickEvt = onMoveRowDownClick(dataView);
    const onCopyRowClickEvt = onCopyRowClick(dataView, true);
    const onPrevRowClickEvt = onPrevRowClick(dataView);
    const onNextRowClickEvt = onNextRowClick(dataView);

    const isMoveRowMenuVisible = getIsMoveRowMenuVisible(dataView);

    const selectedRow = getSelectedRow(dataView);

    const isAddButton = getIsAddButtonVisible(dataView);
    const isDelButton = getIsDelButtonVisible(dataView);
    const isCopyButton = getIsCopyButtonVisible(dataView);
    const showCrudButtons = isAddButton || (isDelButton && !!selectedRow) || (isCopyButton && !!selectedRow)
    const crudButtonsEnabled = getAreCrudButtonsEnabled(dataView);

    const isDialog = !!getOpenedScreen(dataView).dialogInfo;

    const configurationManager = getConfigurationManager(dataView);
    const customTableConfigsExist = configurationManager.customTableConfigurations.length > 0;
    return (
      <Measure bounds={true}>
        {({measureRef, contentRect}) => {
          return (
            <Observer>
              {() => (
                <DataViewHeader
                  domRef={measureRef}
                  isVisible={this.isBarVisible}
                  className={"mobileDataViewHeader"}
                >
                  {this.isBarVisible &&
                    <>
                      <div className="fullspaceBlock">
                        <DataViewHeaderGroup noShrink={true} className={"rowCount"} noDivider={true}>
                          {renderRowCount(this.dataView)}
                        </DataViewHeaderGroup>
                        {isMoveRowMenuVisible ? (
                          <DataViewHeaderGroup isHidden={false} noShrink={true} className={"noDivider"}>
                            <DataViewHeaderAction
                              onMouseDown={onMoveRowUpClickEvt}
                              isDisabled={isRowMovingDisabled}
                            >
                              <Icon
                                src="./icons/move-up.svg"
                                tooltip={T("Move Up", "increase_tool_tip")}
                              />
                            </DataViewHeaderAction>
                            <DataViewHeaderAction
                              onMouseDown={onMoveRowDownClickEvt}
                              isDisabled={isRowMovingDisabled}
                            >
                              <Icon
                                src="./icons/move-down.svg"
                                tooltip={T("Move Down", "decrease_tool_tip")}
                              />
                            </DataViewHeaderAction>
                          </DataViewHeaderGroup>
                        ) : null}
                        {showCrudButtons &&
                          <DataViewHeaderGroup noShrink={true} className={"noDivider"}>
                            {isAddButton && (
                              <DataViewHeaderAction
                                className={"addRow " + (crudButtonsEnabled ? "isGreenHover" : "")}
                                onClick={onCreateRowClickEvt}
                                onShortcut={onCreateRowClickEvt}
                                isDisabled={!crudButtonsEnabled}
                                shortcutPredicate={isAddRecordShortcut}
                              >
                                <Icon src="./icons/add.svg" tooltip={T("Add", "add_tool_tip")}/>
                              </DataViewHeaderAction>
                            )}

                            {isDelButton && !!selectedRow && (
                              <DataViewHeaderAction
                                className="deleteRow isRedHover"
                                onMouseDown={onDeleteRowClickEvt}
                                onShortcut={onDeleteRowClickEvt}
                                shortcutPredicate={isDeleteRecordShortcut}
                              >
                                <Icon
                                  src="./icons/minus.svg"
                                  tooltip={T("Delete", "delete_tool_tip")}
                                />
                              </DataViewHeaderAction>
                            )}

                            {isCopyButton && !!selectedRow && (
                              <DataViewHeaderAction
                                className="copyRow isOrangeHover"
                                onMouseDown={onCopyRowClickEvt}
                                onShortcut={onCopyRowClickEvt}
                                shortcutPredicate={isDuplicateRecordShortcut}
                              >
                                <Icon
                                  src="./icons/duplicate.svg"
                                  tooltip={T("Duplicate", "add_duplicate_tool_tip")}
                                />
                              </DataViewHeaderAction>
                            )}
                          </DataViewHeaderGroup>
                        }
                      </div>
                      {dataView.isFormViewActive() &&
                        <DataViewHeaderGroup noShrink={true}>
                          <DataViewHeaderAction onMouseDown={onPrevRowClickEvt}>
                            <Icon
                              src="./icons/list-arrow-previous.svg"
                              tooltip={T("Previous", "move_prev_tool_tip")}
                            />
                          </DataViewHeaderAction>
                          <DataViewHeaderAction onMouseDown={onNextRowClickEvt}>
                            <Icon
                              src="./icons/list-arrow-next.svg"
                              tooltip={T("Next", "move_next_tool_tip")}
                            />
                          </DataViewHeaderAction>
                        </DataViewHeaderGroup>
                      }
                      {dataView.isTableViewActive() &&
                        <DataViewHeaderGroup noShrink={true}>
                          <DataViewHeaderAction
                            onMouseDown={() => this.onFilterButtonClick()}
                            shortcutPredicate={isFilterRecordShortcut}
                            isActive={isFilterSettingsVisible}
                            className={"test-filter-button"}
                          >
                            <Icon
                              src="./icons/search-filter.svg"
                              tooltip={T("Filter", "filter_tool_tip")}
                            />
                          </DataViewHeaderAction>
                          <FilterDropDown ctx={dataView}/>
                        </DataViewHeaderGroup>
                      }

                      <DataViewHeaderGroup noShrink={true}>
                        <Dropdowner
                          trigger={({refTrigger, setDropped}) => (
                            <DataViewHeaderAction
                              className={"threeDotMenu"}
                              refDom={refTrigger}
                              onMouseDown={() => setDropped(true)}
                              isActive={false}
                            >
                              <Icon
                                src="./icons/dot-menu.svg"
                                tooltip={T("Tools", "tools_tool_tip")}
                              />
                            </DataViewHeaderAction>
                          )}
                          content={({setDropped}) => (
                            <Dropdown>
                              <DropdownItem
                                onClick={(event: any) => {
                                  setDropped(false);
                                  onExportToExcelClickEvt(event);
                                }}
                              >
                                {T("Export to Excel", "excel_tool_tip")}
                              </DropdownItem>
                              <DropdownItem
                                id={"columnConfigItem"}
                                onClick={(event: any) => {
                                  setDropped(false);
                                  this.onColumnConfigurationClick();
                                }}
                              >
                                {T("Column configuration", "column_config_tool_tip")}
                              </DropdownItem>
                              {!isDialog && (
                                <DropdownItem
                                  isDisabled={false}
                                  onClick={(event: any) => {
                                    setDropped(false);
                                    this.onRecordInfoClick(dataView);
                                  }}
                                >
                                  {T("Show record information", "info_button_tool_tip")}
                                </DropdownItem>
                              )}
                              {customTableConfigsExist && [
                                <DropdownItem
                                  isDisabled={false}
                                  isSelected={
                                    configurationManager.defaultTableConfiguration.isActive
                                  }
                                  onClick={async (event: any) => {
                                    setDropped(false);
                                    configurationManager.activeTableConfiguration =
                                      configurationManager.defaultTableConfiguration;
                                    await saveColumnConfigurationsAsync(configurationManager);
                                  }}
                                >
                                  {T("Default View", "default_grid_view_view")}
                                </DropdownItem>,
                                ...configurationManager.customTableConfigurations.map(
                                  (tableConfig) => (
                                    <DropdownItem
                                      key={tableConfig.id}
                                      isDisabled={false}
                                      isSelected={tableConfig.isActive}
                                      onClick={async (event: any) => {
                                        setDropped(false);
                                        configurationManager.activeTableConfiguration = tableConfig;
                                        await saveColumnConfigurationsAsync(configurationManager);
                                      }}
                                    >
                                      {tableConfig.name}
                                    </DropdownItem>
                                  )
                                ),
                                <DropdownItem
                                  isDisabled={false}
                                  onClick={async (event: any) => {
                                    setDropped(false);
                                    await saveColumnConfigurationsAsync(configurationManager);
                                  }}
                                >
                                  {T("Save View", "save_current_column_config")}
                                </DropdownItem>,
                                <DropdownItem
                                  isDisabled={
                                    configurationManager.defaultTableConfiguration.isActive
                                  }
                                  onClick={async (event: any) => {
                                    setDropped(false);
                                    await configurationManager.deleteActiveTableConfiguration();
                                  }}
                                >
                                  {T("Delete View", "delete_current_column_config")}
                                </DropdownItem>,
                              ]}
                              {this.relevantMenuActions.length > 0 && <DropdownDivider/>}
                              {this.renderMenuActions({setMenuDropped: setDropped})}
                            </Dropdown>
                          )}
                        />
                      </DataViewHeaderGroup>
                    </>
                  }
                </DataViewHeader>
              )}
            </Observer>
          );
        }}
      </Measure>
    );
  }
}

export function MobileDataViewHeader(props: { isVisible: boolean }) {
  const extension = useContext(CtxDataViewHeaderExtension);
  return <DataViewHeaderInner isVisible={props.isVisible} extension={extension}/>;
}
