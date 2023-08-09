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

/* eslint-disable @typescript-eslint/no-unused-vars */
import bind from "bind-decorator";
import { action, computed, flow, observable } from "mobx";
import { inject, observer, Provider } from "mobx-react";
import { onTableKeyDown } from "model/actions-ui/DataView/TableView/onTableKeyDown";
import React, { useContext } from "react";
import { onColumnHeaderClick } from "model/actions-ui/DataView/TableView/onColumnHeaderClick";
import { ITablePanelView } from "model/entities/TablePanelView/types/ITablePanelView";
import { IDataView } from "model/entities/types/IDataView";
import { IProperty } from "model/entities/types/IProperty";
import { getColumnHeaders } from "model/selectors/TablePanelView/getColumnHeaders";
import { getSelectedColumnIndex } from "model/selectors/TablePanelView/getSelectedColumnIndex";
import { getTableViewProperties } from "model/selectors/TablePanelView/getTableViewProperties";
import { IColumnHeader } from "model/selectors/TablePanelView/types";
import { FilterSettings } from "gui/Components/ScreenElements/Table/FilterSettings/FilterSettings";
import { Header } from "gui/Components/ScreenElements/Table/Header";
import { RawTable, Table } from "gui/Components/ScreenElements/Table/Table";
import { IGridDimensions } from "gui/Components/ScreenElements/Table/types";
import { TableViewEditor } from "./TableViewEditor";
import { getSelectedRowIndex } from "model/selectors/DataView/getSelectedRowIndex";
import { onNoCellClick } from "model/actions-ui/DataView/TableView/onNoCellClick";
import { onOutsideTableClick } from "model/actions-ui/DataView/TableView/onOutsideTableClick";
import { getFixedColumnsCount } from "model/selectors/TablePanelView/getFixedColumnsCount";
import { getIsSelectionCheckboxesShown } from "model/selectors/DataView/getIsSelectionCheckboxesShown";
import { onColumnWidthChanged } from "model/actions-ui/DataView/TableView/onColumnWidthChanged";
import { onColumnWidthChangeFinished } from "model/actions-ui/DataView/TableView/onColumnWidthChangeFinished";
import { onColumnOrderChangeFinished } from "model/actions-ui/DataView/TableView/onColumnOrderChangeFinished";
import { getGroupingConfiguration } from "model/selectors/TablePanelView/getGroupingConfiguration";
import { getLeadingColumnCount } from "model/selectors/TablePanelView/getLeadingColumnCount";
import { getDataTable } from "model/selectors/DataView/getDataTable";
import { getTablePanelView } from "model/selectors/TablePanelView/getTablePanelView";
import { getFormScreenLifecycle } from "model/selectors/FormScreen/getFormScreenLifecycle";
import { SelectionCheckBoxHeader } from "gui/Components/ScreenElements/Table/SelectionCheckBoxHeader";
import { aggregationToString } from "model/entities/Aggregatioins";
import { getOpenedScreen } from "model/selectors/getOpenedScreen";
import { getIsEditing } from "model/selectors/TablePanelView/getIsEditing";
import { ITableConfiguration } from "model/entities/TablePanelView/types/IConfigurationManager";
import { CtxDataView, DataViewContext } from "gui/Components/ScreenElements/DataView";
import S from "./TableView.module.scss";
import { isMobileLayoutActive } from "model/selectors/isMobileLayoutActive";
import cx from "classnames";
import { getGridFocusManager } from "model/entities/GridFocusManager";

interface ITableViewProps {
  dataView?: IDataView;
  tablePanelView?: ITablePanelView;
  onColumnDialogCancel?: (event: any) => void;
  onColumnDialogOk?: (event: any, configuration: ITableConfiguration) => void;
  onTableKeyDown?: (event: any) => void;
}

@inject(({dataView}) => {
  return {
    dataView,
    tablePanelView: dataView.tablePanelView,
    onColumnDialogCancel: dataView.tablePanelView.onColumnConfCancel,
    onColumnDialogOk: dataView.tablePanelView.onColumnConfSubmit,

    onTableKeyDown: onTableKeyDown(dataView),
  };
})
@observer
export class TableViewInner extends React.Component<ITableViewProps & { dataViewContext?: DataViewContext }> {
  constructor(props: any) {
    super(props);

    this.props.dataView?.initializeNewScrollLoader();
    getGroupingConfiguration(this.props.dataView).registerGroupingOnOffHandler(() => {
      this.props.dataView?.initializeNewScrollLoader();
    });
  }

  componentDidMount() {
    const openScreen = getOpenedScreen(this.props.dataView);
    const dataViews = openScreen.content.formScreen?.dataViews;
    const isMainDataView =
      (dataViews && dataViews.length > 0 && this.props.dataView?.isBindingRoot) ||
      dataViews?.length === 1;

    if (openScreen.isActive && isMainDataView) {
      if (!this.props.dataView?.isFormViewActive()) {
        const tablePanelView = getTablePanelView(this.props.dataView);
        tablePanelView.triggerOnFocusTable();
      }
    }

    window.addEventListener('mousemove', this.handleWindowMouseMove);
    window.addEventListener('keydown', this.handleWindowKeyDown);
    window.addEventListener('keyup', this.handleWindowKeyUp);
  }

  componentWillUnmount() {
    window.removeEventListener('mousemove', this.handleWindowMouseMove);
    window.removeEventListener('keydown', this.handleWindowKeyDown);
    window.removeEventListener('keyup', this.handleWindowKeyUp);
  }

  @action.bound handleWindowMouseMove(event: any) {
    const thisInstance = this;
    flow(function* () {
      if(thisInstance.props.tablePanelView)
        yield* thisInstance.props.tablePanelView.onWindowMouseMove(event)
    })();
  }

  @action.bound handleWindowKeyDown(event: any) {
    const thisInstance = this;
    flow(function* () {
      if(thisInstance.props.tablePanelView)
        yield* thisInstance.props.tablePanelView.onWindowKeyDown(event)
    })();
  }

  @action.bound handleWindowKeyUp(event: any) {
    const thisInstance = this;
    flow(function* () {
      if(thisInstance.props.tablePanelView)
        yield* thisInstance.props.tablePanelView.onWindowKeyUp(event)
    })();
  }

  refTableDisposer: any;
  refTable = (elmTable: RawTable | null) => {
    this.elmTable = elmTable;
    if (elmTable) {
      const d1 = this.props.tablePanelView!.subOnFocusTable(() => {
        const gridFocusManager = getGridFocusManager(this.props.dataView);
        if(gridFocusManager.canFocusTable){
          elmTable.focusTable();
        }
      });
      const d2 = this.props.tablePanelView!.subOnScrollToCellShortest(
        elmTable.scrollToCellShortest
      );
      this.refTableDisposer = () => {
        d1();
        d2();
      };
    } else {
      this.refTableDisposer && this.refTableDisposer();
    }
  };

  elmTable: RawTable | null = null;

  headerRenderer = new HeaderRenderer({
    gridDimensions: this.props.dataView!.gridDimensions,
    tablePanelView: this.props.tablePanelView!,
    getFixedColumnCount: () => getFixedColumnsCount(this.props.tablePanelView),
    getIsSelectionCheckboxes: () => getIsSelectionCheckboxesShown(this.props.tablePanelView),
    dataView: this.props.dataView!,
    getColumnHeaders: () => getColumnHeaders(this.props.dataView),
    getTableViewProperties: () => getTableViewProperties(this.props.dataView),
    onColumnWidthChange: (propertyId, width) =>
      onColumnWidthChanged(this.props.tablePanelView, propertyId, width),
    onColumnOrderChange: (id1, id2) =>
      onColumnOrderChangeFinished(this.props.tablePanelView, id1, id2),
    onColumnOrderAttendantsChange: (idSource, idTarget) =>
      this.onColumnOrderAttendantsChange(idSource, idTarget),
  });

  onColumnOrderAttendantsChange(idSource: string | undefined, idTarget: string | undefined) {
    this.props.tablePanelView!.setColumnOrderChangeAttendants(idSource, idTarget);
    getDataTable(this.props.dataView).unlockAddedRowPosition();
  }

  @action.bound
  handleTableKeyDown(event: any) {
    this.props.onTableKeyDown?.(event);
    this.props.dataViewContext?.handleTableKeyDown(event);
  }

  render() {
    const self = this;
    const isSelectionCheckboxes = getIsSelectionCheckboxesShown(this.props.tablePanelView);
    const editingRowIndex = getSelectedRowIndex(this.props.tablePanelView);
    let editingColumnIndex = getSelectedColumnIndex(this.props.tablePanelView);
    if (editingColumnIndex !== undefined && isSelectionCheckboxes) {
      editingColumnIndex++;
    }
    const fixedColumnCount = this.props.tablePanelView
      ? this.props.tablePanelView.fixedColumnCount || 0
      : 0;

    return (
      <Provider tablePanelView={this.props.tablePanelView}>
        <>
          <Table
            tableRows={this.props.dataView!.tableRows}
            gridDimensions={self.props.dataView!.gridDimensions}
            scrollState={self.props.dataView!.scrollState}
            editingRowIndex={editingRowIndex}
            editingColumnIndex={editingColumnIndex}
            isEditorMounted={getIsEditing(this.props.tablePanelView)}
            isLoading={false}
            fixedColumnCount={fixedColumnCount}
            headerContainers={self.headerRenderer.headerContainers}
            renderEditor={() => (
              <TableViewEditor key={`${editingRowIndex}@${editingColumnIndex}`}/>
            )}
            onNoCellClick={onNoCellClick(this.props.tablePanelView)}
            onOutsideTableClick={onOutsideTableClick(this.props.tablePanelView)}
            onContentBoundsChanged={(bounds) => (this.props.dataView!.contentBounds = bounds)}
            refCanvasMovingComponent={this.props.tablePanelView!.setTableCanvas}
            onKeyDown={this.handleTableKeyDown}
            refTable={this.refTable}
            onFocus={() =>
              (getFormScreenLifecycle(
                this.props.dataView
              ).focusedDataViewId = this.props.dataView?.id)
            }
          />
        </>
      </Provider>
    );
  }
}

export function TableView(props: ITableViewProps) {
  const dataViewContext = useContext(CtxDataView);
  return <TableViewInner {...props} dataViewContext={dataViewContext}/>;
}

interface IHeaderRendererData {
  getTableViewProperties: () => IProperty[];

  tablePanelView: ITablePanelView;
  getColumnHeaders: () => IColumnHeader[];
  getIsSelectionCheckboxes: () => boolean;
  onColumnWidthChange: (id: string, newWidth: number) => void;
  onColumnOrderChange: (id: string, targetId: string) => void;
  onColumnOrderAttendantsChange: (
    idSource: string | undefined,
    idTarget: string | undefined
  ) => void;
  dataView: IDataView;
  gridDimensions: IGridDimensions;
  getFixedColumnCount: () => number;
}

class HeaderRenderer implements IHeaderRendererData {
  constructor(data: IHeaderRendererData) {
    Object.assign(this, data);
  }

  gridDimensions: IGridDimensions = null as any;
  getTableViewProperties: () => IProperty[] = null as any;
  getIsSelectionCheckboxes: () => boolean = null as any;
  dataView: IDataView = null as any;
  getFixedColumnCount = null as any;

  @computed get tableViewPropertiesOriginal() {
    return this.getTableViewProperties();
  }

  @computed get tableViewProperties() {
    return this.tableViewPropertiesOriginal;
  }

  getColumnHeaders: () => IColumnHeader[] = null as any;
  onColumnWidthChange: (id: string, newWidth: number) => void = null as any;
  onColumnOrderChange: (id: string, targetId: string) => void = null as any;
  onColumnOrderAttendantsChange: (
    idSource: string | undefined,
    idTarget: string | undefined
  ) => void = null as any;
  tablePanelView: ITablePanelView = null as any;

  columnOrderChangeSourceId: string | undefined;
  columnOrderChangeTargetId: string | undefined;

  @computed get columnHeaders() {
    return this.getColumnHeaders();
  }

  @computed get isSelectionCheckboxes() {
    return this.getIsSelectionCheckboxes();
  }

  @observable isColumnOrderChanging = false;

  @action.bound handleStartColumnOrderChanging(id: string) {
    this.isColumnOrderChanging = true;
    this.columnOrderChangeSourceId = id;
    this.onColumnOrderAttendantsChange(
      this.columnOrderChangeSourceId,
      this.columnOrderChangeTargetId
    );
  }

  @action.bound handleStopColumnOrderChanging(id: string) {
    this.isColumnOrderChanging = false;
    this.columnOrderChangeSourceId = undefined;
    this.columnOrderChangeTargetId = undefined;
    this.onColumnOrderAttendantsChange(
      this.columnOrderChangeSourceId,
      this.columnOrderChangeTargetId
    );
  }

  @action.bound handlePossibleColumnOrderChange(targetId: string | undefined) {
    this.columnOrderChangeTargetId = targetId;
    this.onColumnOrderAttendantsChange(
      this.columnOrderChangeSourceId,
      this.columnOrderChangeTargetId
    );
  }

  @action.bound handleColumnOrderDrop(targetId: string) {
    this.onColumnOrderChange(this.columnOrderChangeSourceId!, targetId);
    this.onColumnOrderAttendantsChange(
      this.columnOrderChangeSourceId,
      this.columnOrderChangeTargetId
    );
  }

  @computed get headerContainers() {
    const columnDimensions = this.gridDimensions.displayedColumnDimensionsCom;
    const leadingColumnCount = getLeadingColumnCount(this.dataView);
    const selectionCheckBoxesShown = getIsSelectionCheckboxesShown(this.dataView);
    const groupingColumnCount = leadingColumnCount - (selectionCheckBoxesShown ? 1 : 0);
    const dataColumnCount = columnDimensions.length - leadingColumnCount;
    const headerContainers = [];

    if (selectionCheckBoxesShown) {
      headerContainers.push(
        new HeaderContainer({
          header: this.renderSelectionCheckBoxHeader(columnDimensions[0].width),
          isFixed: true,
          width: columnDimensions[0].width,
        })
      );
    }

    let columnsToFix = this.getFixedColumnCount();
    for (let i = 0; i < groupingColumnCount; i++) {
      headerContainers.push(
        new HeaderContainer({
          header: this.renderDummyHeader(columnDimensions[i].width, i),
          isFixed: columnsToFix > i,
          width: columnDimensions[i].width,
        })
      );
    }

    columnsToFix -= groupingColumnCount;
    for (let i = 0; i < dataColumnCount; i++) {
      const columnWidth = columnDimensions[i + leadingColumnCount].width;
      headerContainers.push(
        new HeaderContainer({
          header: this.renderDataHeader({
            columnIndex: i,
            columnWidth: columnWidth,
            isFirst: headerContainers.length === 0,
            isLast: i === dataColumnCount - 1,
            isFixed: columnsToFix > i,
          }),
          isFixed: columnsToFix > i,
          width: columnWidth,
        })
      );
    }

    return headerContainers;
  }

  renderDummyHeader(columnWidth: number, columnIndex: number) {
    return (
      <div key={`dummy-header-key-${columnIndex}`} style={{minWidth: columnWidth + "px"}}></div>
    );
  }

  renderSelectionCheckBoxHeader(columnWidth: number) {
    return (
      <SelectionCheckBoxHeader key="checkboxHeader" width={columnWidth} dataView={this.dataView}/>
    );
  }

  @bind
  renderDataHeader(args: { columnIndex: number; columnWidth: number, isFirst: boolean, isLast: boolean, isFixed: boolean }) {
    const property = this.tableViewProperties[args.columnIndex];
    const header = this.columnHeaders[args.columnIndex];
    return (
      <Provider key={property.id} property={property}>
        <Header
          key={header.id}
          id={header.id}
          isFixed={args.isFixed}
          columnIndex={args.columnIndex}
          isLast={args.isLast}
          isFirst={args.isFirst}
          width={args.columnWidth}
          label={header.label}
          orderingDirection={header.ordering}
          orderingOrder={header.order + 1}
          onColumnWidthChange={this.onColumnWidthChange}
          onColumnWidthChangeFinished={onColumnWidthChangeFinished(this.tablePanelView)}
          onClick={onColumnHeaderClick(this.tablePanelView)}
          isDragDisabled={isMobileLayoutActive(this.tablePanelView)}
          additionalHeaderContent={this.makeAdditionalHeaderContent(header.id, property, args.columnIndex === 0)}
        />
      </Provider>
    );
  }

  makeAdditionalHeaderContent(columnId: string, property: IProperty, autoFocus: boolean) {
    const filterControlsDisplayed = this.tablePanelView.filterConfiguration
      .isFilterControlsDisplayed;
    if (!filterControlsDisplayed && this.dataView.aggregationData.length === 0) {
      return undefined;
    }
    const headerContent: JSX.Element[] = [];
    if (filterControlsDisplayed) {
      headerContent.push(<div className={"filterRow"}><FilterSettings key={`filter-settings-${columnId}`}
                                                                      autoFocus={autoFocus} ctx={this.dataView}/>
      </div>);
    }
    if (this.dataView.aggregationData.length !== 0) {
      const aggregation = this.dataView.aggregationData.find((agg) => agg.columnId === columnId);
      if (aggregation) {
        headerContent.push(
          <div className={cx(S.aggregationRow, "headerClickable")} key={`aggregation-field-${columnId}`}>
            {aggregationToString(aggregation, property)}
          </div>
        );
      }
    }
    return () => <>{headerContent}</>;
  }
}

export interface IHeaderContainer {
  header: JSX.Element;
  isFixed: boolean;
  width: number;
}

class HeaderContainer implements IHeaderContainer {
  constructor(data: IHeaderContainer) {
    Object.assign(this, data);
  }

  header: JSX.Element = null as any;
  isFixed: boolean = null as any;
  width: number = null as any;
}
