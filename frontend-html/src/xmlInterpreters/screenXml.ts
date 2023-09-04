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

import { DataViewLifecycle } from "model/entities/DataViewLifecycle/DataViewLifecycle";
import { LookupLoader } from "model/entities/LookupLoader";
import { RowState } from "model/entities/RowState";
import { Action } from "model/entities/Action";
import { ActionParameter } from "model/entities/ActionParameter";
import { ComponentBinding, ComponentBindingPair } from "model/entities/ComponentBinding";
import { DataSource } from "model/entities/DataSource";
import { DataSourceField } from "model/entities/DataSourceField";
import { DataTable } from "model/entities/DataTable";
import { DataView } from "model/entities/DataView";
import { DropDownColumn } from "model/entities/DropDownColumn";
import { FilterConfiguration } from "model/entities/FilterConfiguration";
import { FormPanelView } from "model/entities/FormPanelView/FormPanelView";
import { FormScreen } from "model/entities/FormScreen";
import { Lookup } from "model/entities/Lookup";
import { OrderingConfiguration } from "model/entities/OrderingConfiguration";
import { Property } from "model/entities/Property";
import { ColumnConfigurationModel } from "model/entities/TablePanelView/ColumnConfigurationModel";
import { TablePanelView } from "model/entities/TablePanelView/TablePanelView";
import { IComponentBinding } from "model/entities/types/IComponentBinding";
import { IFormScreenLifecycle02 } from "model/entities/types/IFormScreenLifecycle";
import { IPanelViewType } from "model/entities/types/IPanelViewType";
import { findActions, findFormPropertyIds, findFormRoot, findParameters, findStopping, findUIRoot } from "./xmlUtils";
import { GroupingConfiguration } from "model/entities/GroupingConfiguration";
import { ServerSideGrouper } from "model/entities/ServerSideGrouper";
import { ClientSideGrouper } from "model/entities/ClientSideGrouper";
import $root from "rootContainer";
import { SCOPE_Screen } from "modules/Screen/ScreenModule";
import { SCOPE_DataView } from "modules/DataView/DataViewModule";
import { scopeFor, TypeSymbol } from "dic/Container";
import { SCOPE_FormPerspective } from "modules/DataView/Perspective/FormPerspective/FormPerspectiveModule";
import { IFormPerspectiveDirector } from "modules/DataView/Perspective/FormPerspective/FormPerspectiveDirector";
import { SCOPE_TablePerspective } from "modules/DataView/Perspective/TablePerspective/TablePerspectiveModule";
import { ITablePerspectiveDirector } from "modules/DataView/Perspective/TablePerspective/TablePerspectiveDirector";
import { IPerspective } from "modules/DataView/Perspective/Perspective";
import { flow } from "mobx";
import { IViewConfiguration, ViewConfiguration } from "modules/DataView/ViewConfiguration";
import { saveColumnConfigurations } from "model/actions/DataView/TableView/saveColumnConfigurations";
import { IPanelConfiguration } from "model/entities/types/IPanelConfiguration";
import { parseToOrdering } from "model/entities/types/IOrderingConfiguration";
import { isInfiniteScrollingActive } from "model/selectors/isInfiniteScrollingActive";
import { cssString2Object } from "utils/objects";
import { TreeDataTable } from "model/entities/TreeDataTable";
import { getDataStructureEntityId } from "model/selectors/DataView/getDataStructureEntityId";
import { getEntity } from "model/selectors/DataView/getEntity";
import { DataViewAPI } from "modules/DataView/DataViewAPI";
import { getSelectedRowId } from "model/selectors/TablePanelView/getSelectedRowId";
import { IRowCursor, RowCursor } from "modules/DataView/TableCursor";
import { getDataViewPropertyById } from "model/selectors/DataView/getDataViewPropertyById";
import { DataViewData } from "modules/DataView/DataViewData";
import { ScreenAPI } from "modules/Screen/ScreenAPI";
import { getMenuItemId } from "model/selectors/getMenuItemId";
import { getSessionId } from "model/selectors/getSessionId";
import { getApi } from "model/selectors/getApi";
import { getWorkbench } from "model/selectors/getWorkbench";
import { SCOPE_FormScreen } from "modules/Screen/FormScreen/FormScreenModule";
import { IOrigamAPI, OrigamAPI } from "model/entities/OrigamAPI";
import { IDataView as IDataViewTS } from "modules/DataView/DataViewTypes";
import { createIndividualLookupEngine } from "modules/Lookup/LookupModule";
import { IProperty } from "model/entities/types/IProperty";
import { SCOPE_MapPerspective } from "modules/DataView/Perspective/MapPerspective/MapPerspectiveModule";
import { IMapPerspectiveDirector } from "modules/DataView/Perspective/MapPerspective/MapPerspectiveDirector";
import {
  MapLayer as MapLayerSetup,
  MapSetupStore,
} from "modules/DataView/Perspective/MapPerspective/stores/MapSetupStore";
import { MapRootStore } from "modules/DataView/Perspective/MapPerspective/stores/MapRootStore";
import { IFormPerspective } from "modules/DataView/Perspective/FormPerspective/FormPerspective";
import { addFilterGroups } from "./filterXml";
import { FilterGroupManager } from "model/entities/FilterGroupManager";
import { getGroupingConfiguration } from "model/selectors/TablePanelView/getGroupingConfiguration";
import { ITablePerspective } from "modules/DataView/Perspective/TablePerspective/TablePerspective";
import { runGeneratorInFlowWithHandler } from "utils/runInFlowWithHandler";
import { createConfigurationManager } from "xmlInterpreters/createConfigurationManager";
import { getMomentFormat, replaceDefaultDateFormats } from "./getMomentFormat";
import { getTablePanelView } from "../model/selectors/TablePanelView/getTablePanelView";
import { isMobileLayoutActive } from "model/selectors/isMobileLayoutActive";
import { ScreenFocusManager } from "model/entities/ScreenFocusManager";
import { getWorkbenchLifecycle } from "model/selectors/getWorkbenchLifecycle";


function getPropertyParameters(node: any) {
  const parameters = findParameters(node);
  const result: { [key: string]: any } = {};
  for (let p of parameters) {
    result[p.attributes.Name] = p.attributes.Value;
  }
  return result;
}

const instance2XmlNode = new WeakMap<any, any>();

export function fixColumnWidth(width: number) {
  // Sometimes they send us negative width, which destroys table rendering.
  if (isNaN(width)) {
    return 100;
  } else {
    return Math.abs(width);
  }
}

function parseProperty(node: any, idx: number): IProperty {
  const propertyObject = new Property({
    xmlNode: node,
    id: node.attributes.Id,
    tabIndex: node.attributes.TabIndex,
    controlPropertyId: node.attributes.ControlPropertyId,
    controlPropertyValue: node.attributes.ControlPropertyValue,
    modelInstanceId: node.attributes.ModelInstanceId || "",
    name: node.attributes.Name,
    readOnly: node.attributes.ReadOnly === "true",
    x: parseInt(node.attributes.X, 10),
    y: parseInt(node.attributes.Y, 10),
    width: parseInt(node.attributes.Width, 10),
    height: parseInt(node.attributes.Height, 10),
    captionLength: parseInt(node.attributes.CaptionLength, 10),
    captionPosition: node.attributes.CaptionPosition,
    entity: node.attributes.Entity,
    column: node.attributes.Column,
    alwaysHidden: node.attributes.AlwaysHidden === "true",
    parameters: getPropertyParameters(node),
    dock: node.attributes.Dock,
    multiline: node.attributes.Multiline === "true",
    isAllowTab: node.attributes.AllowTab === "true",
    isPassword: node.attributes.IsPassword === "true",
    isRichText: node.attributes.IsRichText === "true",
    autoSort: node.attributes.AutoSort === "true",
    maxLength: parseInt(node.attributes.MaxLength, 10),
    modelFormatterPattern: replaceDefaultDateFormats(node.attributes.FormatterPattern),
    formatterPattern: node.attributes.FormatterPattern
      ? getMomentFormat(node)
      : "",
    customNumericFormat: node.attributes.CustomNumericFormat,
    identifier: node.attributes.Identifier,
    gridColumnWidth: node.attributes.GridColumnWidth
      ? parseInt(node.attributes.GridColumnWidth)
      : 100,
    columnWidth: fixColumnWidth(
      node.attributes.GridColumnWidth ? parseInt(node.attributes.GridColumnWidth) : 100
    ),
    suppressEmptyColumns: node.attributes.SuppressEmptyColumns === "true",
    lookupId: node.attributes.LookupId,
    lookup: !node.attributes.LookupId
      ? undefined
      : new Lookup({
        dropDownShowUniqueValues: node.attributes.DropDownShowUniqueValues === "true",
        lookupId: node.attributes.LookupId,
        identifier: node.attributes.Identifier,
        identifierIndex: parseInt(node.attributes.IdentifierIndex, 10),
        dropDownType: node.attributes.DropDownType,
        cached: node.attributes.Cached === "true",
        searchByFirstColumnOnly: node.attributes.SearchByFirstColumnOnly === "true",
        dropDownColumns: findStopping(node, (n) => n.name === "Property").map(
          (ddProperty) => {
            return new DropDownColumn({
              id: ddProperty.attributes.Id,
              name: ddProperty.attributes.Name,
              column: ddProperty.attributes.Column,
              entity: ddProperty.attributes.Entity,
              index: parseInt(ddProperty.attributes.Index, 10),
            });
          }
        ),
        dropDownParameters: findStopping(
          node,
          (n) => n.name === "ComboBoxParameterMapping"
        ).map((ddParam) => {
          return {
            parameterName: ddParam.attributes.ParameterName,
            fieldName: ddParam.attributes.FieldName,
          };
        }),
      }),

    allowReturnToForm: node.attributes.AllowReturnToForm === "true",
    isTree: node.attributes.IsTree === "true",
    isAggregatedColumn: node.attributes.Aggregated || false,
    isLookupColumn: node.attributes.IsLookupColumn || false,
    style: cssString2Object(node.attributes.Style),
    toolTip: node.elements.find((child: any) => child.name === "ToolTip")?.elements?.[0]?.text,
    supportsServerSideSorting: node.attributes.SupportsServerSideSorting === "true",
    fieldType: node.attributes.FieldType
  });
  if (node.elements && node.elements.length > 0) {
    node.elements
      .filter((element: any) => element.name === "Property")
      .map((childProperty: any, idx: number) => parseProperty(childProperty, idx))
      .forEach((childProperty: IProperty) => {
        childProperty.parent = propertyObject;
        childProperty.x = childProperty.x + propertyObject.x;
        childProperty.y = propertyObject.y;
        propertyObject.childProperties.push(childProperty);
      });
  }
  return propertyObject;
}

export function*interpretScreenXml(
  screenDoc: any,
  formScreenLifecycle: IFormScreenLifecycle02,
  panelConfigurationsRaw: any,
  lookupMenuMappings: any,
  sessionId: string,
  workflowTaskId: string | null,
  isLazyLoading: boolean
) {
  const workbench = getWorkbench(formScreenLifecycle);
  const workbenchLifeCycle = getWorkbenchLifecycle(formScreenLifecycle);
  const $workbench = scopeFor(workbench);
  const panelConfigurations = new Map<string, IPanelConfiguration>(
    panelConfigurationsRaw.map((pcr: any) => [
      pcr.panel.instanceId,
      {
        position: pcr.position,
        defaultOrdering: parseToOrdering(pcr.defaultSort),
      },
    ])
  );

  const dataSourcesXml = findStopping(screenDoc, (n) => n.name === "DataSources")[0];

  const windowXml = findStopping(screenDoc, (n) => n.name === "Window")[0];

  const dataViews = findStopping(
    screenDoc,
    (n) =>
      (n.name === "UIElement" || n.name === "UIRoot") &&
      (n.attributes.Type === "Grid" ||
        n.attributes.Type === "TreePanel" ||
        n.attributes.Type === "SectionLevelPlugin" ||
        n.attributes.Type === "ScreenLevelPluginData")
  );

  checkInfiniteScrollWillWork(dataViews, formScreenLifecycle, panelConfigurations);

  function panelViewFromNumber(pvn: number) {
    switch (pvn) {
      case 1:
      default:
        return IPanelViewType.Table;
      case 0:
        return IPanelViewType.Form;
      case 5:
        return IPanelViewType.Map;
    }
  }

  const xmlComponentBindings = findStopping(
    screenDoc,
    (n) => n.name === "Binding" && n.parent.name === "ComponentBindings"
  );

  const screenAPI = new ScreenAPI(
    () => getSessionId(scr),
    () => getMenuItemId(scr),
    () => getApi(scr)
  );

  const componentBindings: IComponentBinding[] = [];

  for (let xmlBinding of xmlComponentBindings) {
    let existingBinding = componentBindings.find(
      (item) =>
        item.parentId === xmlBinding.attributes.ParentId &&
        item.childId === xmlBinding.attributes.ChildId
    );
    const componentBindingPair = new ComponentBindingPair({
      parentPropertyId: xmlBinding.attributes.ParentProperty,
      childPropertyId: xmlBinding.attributes.ChildProperty,
    });
    if (existingBinding) {
      existingBinding.bindingPairs.push(componentBindingPair);
    } else {
      componentBindings.push(
        new ComponentBinding({
          parentId: xmlBinding.attributes.ParentId,
          childId: xmlBinding.attributes.ChildId,
          parentEntity: xmlBinding.attributes.ParentEntity,
          childEntity: xmlBinding.attributes.ChildEntity,
          bindingPairs: [componentBindingPair],
          childPropertyType: xmlBinding.attributes.ChildPropertyType,
        })
      );
    }
  }

  const foundLookupIds = new Set<string>();
  const uiRoot = findUIRoot(windowXml);

  const focusManager = new ScreenFocusManager();
  const scr = new FormScreen({
    title: windowXml.attributes.Title,
    focusManager: focusManager,
    uiRootType: uiRoot.attributes.Type,
    menuId: windowXml.attributes.MenuId,
    dynamicTitleSource: screenDoc.elements[0].attributes.DynamicFormLabelSource,
    sessionId,
    autoWorkflowNext: screenDoc.elements[0].attributes.AutoWorkflowNext === "true",
    openingOrder: 0,
    suppressSave: windowXml.attributes.SuppressSave === "true",
    suppressRefresh: windowXml.attributes.SuppressRefresh === "true",
    showInfoPanel: windowXml.attributes.ShowInfoPanel === "true",
    showWorkflowNextButton: windowXml.attributes.ShowWorkflowNextButton === "true",
    showWorkflowCancelButton: windowXml.attributes.ShowWorkflowCancelButton === "true",
    autoRefreshInterval: parseInt(windowXml.attributes.AutoRefreshInterval, 10),
    refreshOnFocus: windowXml.attributes.RefreshOnFocus === "true",
    cacheOnClient: windowXml.attributes.CacheOnClient === "true",
    autoSaveOnListRecordChange: windowXml.attributes.AutoSaveOnListRecordChange === "true",
    requestSaveAfterUpdate: windowXml.attributes.RequestSaveAfterUpdate === "true",
    screenUI: screenDoc,
    workflowTaskId: workflowTaskId,
    panelConfigurations,
    formScreenLifecycle,
    // isSessioned: windowXml.attributes.UseSession,
    dataSources: dataSourcesXml.elements.map((dataSource: any) => {
      return new DataSource({
        rowState: new RowState(workbenchLifeCycle.portalSettings?.rowStatesDebouncingDelayMilliseconds),
        entity: dataSource.attributes.Entity,
        dataStructureEntityId: dataSource.attributes.DataStructureEntityId,
        identifier: dataSource.attributes.Identifier,
        lookupCacheKey: dataSource.attributes.LookupCacheKey,
        fields: findStopping(dataSource, (n) => n.name === "Field").map((field) => {
          return new DataSourceField({
            index: parseInt(field.attributes.Index, 10),
            name: field.attributes.Name,
          });
        }),
      });
    }),

    dataViews: dataViews.map((dataView, i) => {
      const configuration = findStopping(dataView, (n) => n.name === "Configuration");

      const properties = findStopping(dataView, (n) => n.name === "Property").map(parseProperty);

      const formPropertyIds = new Set(findFormPropertyIds(dataView));
      for (let prop of properties) {
        if (formPropertyIds.has(prop.id)) {
          prop.isFormField = true;
        }
      }

      const actions = findActions(dataView).map(
        (action) =>
          new Action({
            id: action.attributes.Id,
            caption: action.attributes.Caption,
            groupId: action.attributes.GroupId,
            type: action.attributes.Type,
            iconUrl: action.attributes.IconUrl,
            mode: action.attributes.Mode,
            isDefault: action.attributes.IsDefault === "true",
            placement: action.attributes.Placement,
            confirmationMessage: action.attributes.ConfirmationMessage,
            parameters: findParameters(action).map(
              (parameter) =>
                new ActionParameter({
                  name: parameter.attributes.Name,
                  fieldName: parameter.attributes.FieldName,
                })
            ),
          })
      );
      const defaultOrderings = panelConfigurations.get(dataView.attributes.ModelInstanceId)
        ?.defaultOrdering;
      if (defaultOrderings) {
        for (let ordering of defaultOrderings) {
          ordering.lookupId = properties.find((prop) => prop.id === ordering.columnId)?.lookupId;
        }
      }

      const orderingConfiguration = new OrderingConfiguration(defaultOrderings);
      const implicitFilters = getImplicitFilters(dataView);

      const filterConfiguration = new FilterConfiguration(implicitFilters);
      const filterGroupManager = new FilterGroupManager(filterConfiguration);
      panelConfigurationsRaw
        .filter((conf: any) => conf.panel.instanceId === dataView.attributes.ModelInstanceId)
        .forEach((conf: any) => addFilterGroups(filterGroupManager, properties, conf));

      const dataViewInstance: DataView = new DataView({
        isFirst: i === 0,
        id: dataView.attributes.Id,
        focusManager: focusManager,
        attributes: dataView.attributes,
        type: dataView.attributes.Type,
        modelInstanceId: dataView.attributes.ModelInstanceId,
        name: dataView.attributes.Name,
        modelId: dataView.attributes.ModelId,
        newRecordView: dataView.attributes.NewRecordView,
        defaultPanelView: panelViewFromNumber(parseInt(dataView.attributes.DefaultPanelView)),
        activePanelView: panelViewFromNumber(parseInt(dataView.attributes.DefaultPanelView)),
        isMapSupported: dataView.attributes.IsMapSupported === "true",
        isHeadless: dataView.attributes.IsHeadless === "true",
        disableActionButtons: dataView.attributes.DisableActionButtons === "true",
        showAddButton: dataView.attributes.ShowAddButton === "true",
        hideCopyButton: dataView.attributes.HideCopyButton === "true",
        showDeleteButton: dataView.attributes.ShowDeleteButton === "true",
        showSelectionCheckboxesSetting: dataView.attributes.ShowSelectionCheckboxes === "true",
        isGridHeightDynamic: dataView.attributes.IsGridHeightDynamic === "true",
        selectionMember: dataView.attributes.SelectionMember,
        orderMember: dataView.attributes.OrderMember,
        isDraggingEnabled: dataView.attributes.IsDraggingEnabled === "true",
        entity: dataView.attributes.Entity,
        dataMember: dataView.attributes.DataMember,
        isRootGrid: dataView.attributes.IsRootGrid === "true",
        isRootEntity: dataView.attributes.IsRootEntity === "true",
        isPreloaded: dataView.attributes.IsPreloaded === "true",
        requestDataAfterSelectionChange:
          dataView.attributes.RequestDataAfterSelectionChange === "true",
        confirmSelectionChange: dataView.attributes.ConfirmSelectionChange === "true",
        formViewUI: findFormRoot(dataView),
        dataTable:
          dataView.attributes.Type === "TreePanel"
            ? new TreeDataTable(
              dataView.attributes.IdProperty,
              dataView.attributes.ParentIdProperty
            )
            : new DataTable({
              formScreenLifecycle: formScreenLifecycle,
              dataViewAttributes: dataView.attributes,
              orderingConfiguration: orderingConfiguration,
              filterConfiguration: filterConfiguration,
            }),
        serverSideGrouper: new ServerSideGrouper(),
        lifecycle: new DataViewLifecycle(),
        tablePanelView: new TablePanelView({
          tablePropertyIds: properties
            .filter(x => !x.alwaysHidden)
            .map((prop) => prop.id),
          columnConfigurationModel: new ColumnConfigurationModel(),
          filterConfiguration: filterConfiguration,
          filterGroupManager: filterGroupManager,
          orderingConfiguration: orderingConfiguration,
          groupingConfiguration: new GroupingConfiguration(),
          rowHeight: 25,
        }),
        formPanelView: new FormPanelView(),
        lookupLoader: new LookupLoader(),
        properties,
        actions,
        clientSideGrouper: new ClientSideGrouper(),
        dataViewData: new DataViewData(
          () => dataViewInstance.dataTable,
          (propId) => getDataViewPropertyById(dataViewInstance, propId)
        ),
        dataViewRowCursor: new RowCursor(() => getSelectedRowId(dataViewInstance)),
        dataViewApi: new DataViewAPI(
          () => getDataStructureEntityId(dataViewInstance),
          () => getEntity(dataViewInstance),
          () => screenAPI
        ),
      });

      instance2XmlNode.set(dataViewInstance, dataView);

      const gridConfigurationNodes = configuration.filter(
        (node) => node?.parent?.attributes?.Type === "Grid" || node?.parent?.attributes?.Type === "SectionLevelPlugin"
      );
      const configurationNode =
        gridConfigurationNodes.length === 1 ? gridConfigurationNodes[0] : undefined;
      if (configurationNode) {
        const defaultView = findStopping(
          configurationNode,
          (n) => n.name === "view" && n.parent.name === "defaultView"
        );
        defaultView.forEach((element) => {
          if (!dataViewInstance.isHeadless && element.attributes.id.length <= 2) {
            dataViewInstance.activePanelView = element.attributes.id;
          }
        });
      }
      const configurationManager = createConfigurationManager(
        gridConfigurationNodes,
        dataViewInstance.tablePanelView.tableProperties,
        isLazyLoading
      );
      dataViewInstance.tablePanelView.configurationManager = configurationManager;
      configurationManager.parent = dataViewInstance.tablePanelView;
      properties
        .filter((prop) => prop.gridColumnWidth < 0)
        .forEach((prop) => {
          prop.gridColumnWidth = Math.abs(prop.gridColumnWidth);
          prop.width = Math.abs(prop.width);
          dataViewInstance.tablePanelView.setPropertyHidden(prop.id, true);
          const conf = configurationManager.defaultTableConfiguration.columnConfigurations.find(
            (item) => item.propertyId === prop.id
          );
          if (conf) {
            conf.isVisible = false;
          }
        });


      lookupMenuMappings.forEach((mapping: any) => {
        if (mapping.lookupId && (mapping.menuId || mapping.dependsOnValue)) {
          properties.forEach((property) => {
            if (property.lookup && property.lookup.lookupId === mapping.lookupId) {
              property.linkToMenuId = mapping.menuId;
              property.linkDependsOnValue = mapping.dependsOnValue;
            }
          });
        }
      });

      // COLUMN ORDER

      return dataViewInstance;
    }),
    componentBindings: [],
  });

  for (let xmlBinding of xmlComponentBindings) {
    let existingBinding = scr.componentBindings.find(
      (item) =>
        item.parentId === xmlBinding.attributes.ParentId &&
        item.childId === xmlBinding.attributes.ChildId
    );
    const componentBindingPair = new ComponentBindingPair({
      parentPropertyId: xmlBinding.attributes.ParentProperty,
      childPropertyId: xmlBinding.attributes.ChildProperty,
    });
    if (existingBinding) {
      existingBinding.bindingPairs.push(componentBindingPair);
    } else {
      const cb = new ComponentBinding({
        parentId: xmlBinding.attributes.ParentId,
        childId: xmlBinding.attributes.ChildId,
        parentEntity: xmlBinding.attributes.ParentEntity,
        childEntity: xmlBinding.attributes.ChildEntity,
        bindingPairs: [componentBindingPair],
        childPropertyType: xmlBinding.attributes.ChildPropertyType,
      });
      scr.componentBindings.push(cb);
      cb.parent = scr;
    }
  }

  const $screen = $workbench!.beginLifetimeScope(SCOPE_Screen);
  const $formScreen = $screen.beginLifetimeScope(SCOPE_FormScreen);
  $root.register(IOrigamAPI, () => getApi(scr) as OrigamAPI).scopedInstance(SCOPE_Screen);

  const IFormScreen = TypeSymbol<FormScreen>("IFormScreen");

  $formScreen.register(IFormScreen, () => scr).scopedInstance(SCOPE_Screen);

  for (let dataView of scr.dataViews) {
    const $dataView = $formScreen.beginLifetimeScope(SCOPE_DataView);
    $dataView.register(IDataViewTS, () => dataView as DataView).scopedInstance(SCOPE_DataView);

    $dataView
      .register(IRowCursor, () => new RowCursor(() => getSelectedRowId(dataView)))
      .scopedInstance(SCOPE_DataView);
    $dataView
      .register(
        IViewConfiguration,
        () =>
          new ViewConfiguration(
            function*(perspectiveTag) {
              dataView.activePanelView = perspectiveTag as any;
              if(!isMobileLayoutActive(dataView)){
                yield*saveColumnConfigurations(dataView)();
              }
            },
            () => {
              if (
                dataView.activePanelView === IPanelViewType.Form &&
                getGroupingConfiguration(dataView).isGrouping &&
                isLazyLoading
              ) {
                return IPanelViewType.Table;
              }
              return dataView.activePanelView;
            }
          )
      )
      .scopedInstance(SCOPE_DataView);

    $dataView.resolve(IDataViewTS);

    const $tablePerspective = $dataView.beginLifetimeScope(SCOPE_TablePerspective);
    $tablePerspective.resolve(ITablePerspectiveDirector).setup();
    const tablePerspective = $tablePerspective.resolve(ITablePerspective);
    tablePerspective.onTablePerspectiveShown = () => getTablePanelView(dataView)?.triggerOnFocusTable();

    const $formPerspective = $dataView.beginLifetimeScope(SCOPE_FormPerspective);
    $formPerspective.resolve(IFormPerspectiveDirector).setup();
    const formPerspective = $formPerspective.resolve(IFormPerspective);
    dataView.activateFormView = formPerspective.handleClick.bind(formPerspective);
    dataView.activateTableView = () =>
      runGeneratorInFlowWithHandler({
        ctx: dataView,
        generator: tablePerspective.handleToolbarBtnClick.bind(formPerspective)(),
      });
    dataView.isFormViewActive = () => formPerspective.isActive;
    dataView.isTableViewActive = () => tablePerspective.isActive;
    if (dataView.isMapSupported) {
      const dataViewXmlNode = instance2XmlNode.get(dataView)!;
      const rootStore = new MapRootStore(dataView);
      populateMapViewSetup(rootStore.mapSetupStore, dataViewXmlNode);
      //const isReadonly = dataView.properties.some((prop) => prop.readOnly);
      const isReadonly = !!dataView.properties.find(
        (prop) => prop.id === rootStore.mapSetupStore.mapLocationMember
      )?.readOnly;
      rootStore.mapSetupStore.isReadOnlyView = isReadonly;
      const $mapPerspective = $dataView.beginLifetimeScope(SCOPE_MapPerspective);
      const mapPerspectiveDirector = $mapPerspective.resolve(IMapPerspectiveDirector);

      mapPerspectiveDirector.rootStore = rootStore;
      mapPerspectiveDirector.setup();
    }

    //***************** */

    const {lookupMultiEngine} = workbench;

    dataView.properties
      .flatMap((property) => [property, ...property.childProperties])
      .filter((property) => property.isLookup && property.lookupId)
      .forEach((property) => {
        const {lookupId} = property;
        foundLookupIds.add(lookupId!);
        if (!lookupMultiEngine.lookupEngineById.has(lookupId!)) {
          const lookupIndividualEngine = createIndividualLookupEngine(lookupId!, lookupMultiEngine);
          lookupMultiEngine.lookupCleanerReloaderById.set(
            lookupId!,
            lookupIndividualEngine.lookupCleanerReloader
          );
          lookupIndividualEngine.startup();
          lookupMultiEngine.lookupEngineById.set(lookupId!, lookupIndividualEngine);
        }
        const lookupIndividualEngine = lookupMultiEngine.lookupEngineById.get(lookupId!)!;
        property.lookupEngine = lookupIndividualEngine;
      });

    flow($dataView.resolve(IPerspective).activateDefault)();
  }

  $formScreen.resolve(IFormScreen); // Hack to associate FormScreen with its scope to dispose it later.

  return {formScreen: scr, foundLookupIds};
}

function getImplicitFilters(dataViewXml: any) {
  const filterNodes = findStopping(dataViewXml, (node) => node.name === "Filter");
  if (filterNodes.length === 0) {
    return [];
  }
  return filterNodes.flatMap((node) =>
    node.elements.map((element: any) => {
      return {
        propertyId: element.attributes.Property,
        operatorCode: element.attributes.Operator,
        value: element.attributes.RightSideValue,
      };
    })
  );
}

function checkInfiniteScrollWillWork(
  dataViews: any[],
  formScreenLifecycle: IFormScreenLifecycle02,
  panelConfigurations: Map<string, IPanelConfiguration>
) {
  for (let dataView of dataViews) {
    const id = dataView.attributes.ModelInstanceId;
    const panelConfig = panelConfigurations.get(id);
    if (isInfiniteScrollingActive(formScreenLifecycle, dataView.attributes) && !panelConfig) {
      throw new Error(
        `Table in: ${id} has undefined default ordering while infinite scrolling is on. Make sure the underlying DataStructure has a SortSet defined.`
      );
    }
  }
}

function populateMapViewSetup(mss: MapSetupStore, xmlNode: any) {
  const attr = xmlNode.attributes;
  mss.mapAzimuthMember = attr.MapAzimuthMember;
  mss.mapCenterRaw = attr.MapCenter;
  mss.mapColorMember = attr.MapColorMember;
  mss.mapIconMember = attr.MapIconMember;
  mss.mapLocationMember = attr.MapLocationMember;
  mss.mapTextMember = attr.MapTextMember;
  mss.textColorMember = attr.TextColorMember;
  mss.textLocationMember = attr.TextLocationMember;
  mss.textRotationMember = attr.TextRotationMember;
  mss.mapResolutionRaw = attr.MapResolution;

  const layerXmlNodes = findStopping(
    xmlNode,
    (node) => node.name === "Layer" && node.parent.name === "MapViewLayers"
  );
  for (let layerXmlNode of layerXmlNodes) {
    const layerAttr = layerXmlNode.attributes;
    const mls = new MapLayerSetup();
    mss.layers.push(mls);
    mls.defaultEnabled = layerAttr.defaultEnabled === "true";
    mls.id = layerAttr.id;
    mls.title = layerAttr.title;
    mls.type = layerAttr.type;
    const parameterXmlNodes = findStopping(layerXmlNode, (node) => node.name === "Parameter");
    for (let parameterXmlnode of parameterXmlNodes) {
      mls.mapLayerParameters.set(
        parameterXmlnode.attributes.name,
        parameterXmlnode.attributes.value
      );
    }
  }
}
