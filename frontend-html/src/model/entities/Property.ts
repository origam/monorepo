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

import { IDockType, IProperty, IPropertyData } from "./types/IProperty";
import { ICaptionPosition } from "./types/ICaptionPosition";
import { IPropertyColumn } from "./types/IPropertyColumn";
import { action, computed, observable } from "mobx";

import { ILookup } from "./types/ILookup";
import { getDataSourceFieldByName } from "model/selectors/DataSources/getDataSourceFieldByName";
import { IDataSourceField } from "./types/IDataSourceField";
import { LookupResolver } from "modules/Lookup/LookupResolver";
import { LookupLabelsCleanerReloader } from "modules/Lookup/LookupCleanerLoader";
import { getDataTable } from "model/selectors/DataView/getDataTable";

export class Property implements IProperty {
  $type_IProperty: 1 = 1;

  constructor(data: IPropertyData) {
    Object.assign(this, data);
    if (this.lookup) {
      this.lookup.parent = this;
    }
  }

  autoSort: boolean = false;
  id: string = "";
  tabIndex: string | undefined;
  modelInstanceId: string = "";
  name: string = "";
  nameOverride: string | null | undefined = null;
  readOnly: boolean = false;
  x: number = 0;
  y: number = 0;
  width: number = 0;
  height: number = 0;
  captionLength: number = 0;
  captionPosition?: ICaptionPosition;
  entity: string = "";
  column: IPropertyColumn = IPropertyColumn.Text;
  dock?: IDockType | undefined;
  multiline: boolean = false;
  isAllowTab: boolean = false;
  isPassword: boolean = false;
  isRichText: boolean = false;
  maxLength: number = 0;
  allowReturnToForm?: boolean | undefined;
  isTree?: boolean | undefined;
  formatterPattern: string = "";
  modelFormatterPattern: string = "";
  customNumericFormat: string = "";
  gridColumnWidth: number = 100;
  @observable columnWidth: number = 100;
  identifier?: string;
  lookup?: ILookup;
  lookupId?: string;
  lookupEngine?: ILookupIndividualEngine = null as any;
  childProperties: IProperty[] = [];
  isAggregatedColumn: boolean = false;
  isLookupColumn: boolean = false;
  style: any;
  controlPropertyId?: string;
  toolTip: string = null as any;
  suppressEmptyColumns: boolean = false;
  supportsServerSideSorting: boolean = false;

  linkToMenuId?: string = undefined;
  linkDependsOnValue: boolean = false;
  fieldType: string = null as any;
  isFormField: boolean = false;
  alwaysHidden: boolean = false;

  get isLookup() {
    return !!this.lookup;
  }

  get isLink() {
    return !!this.linkToMenuId || this.linkDependsOnValue;
  }

  @action.bound setColumnWidth(width: number) {
    this.columnWidth = width;
  }

  @computed get dataSourceIndex(): number {
    return this.dataSourceField.index;
  }

  @computed get dataIndex() {
    return this.dataSourceIndex;
  }

  @computed get dataSourceField(): IDataSourceField {
    return getDataSourceFieldByName(this, this.id)!;
  }

  @action.bound
  stop() {

  }

  parent: any;
  xmlNode = undefined;

  getPolymophicProperty(row: any[]): IProperty {
    const dataSourceField = getDataSourceFieldByName(this, this.controlPropertyId!)!;
    const controlPropertyValue = getDataTable(this)
      .getCellValueByDataSourceField(row, dataSourceField);
    return this.childProperties
        .find(prop => prop.controlPropertyValue === controlPropertyValue)
      ?? this;
  }
}


export interface ILookupIndividualEngine {
  lookupResolver: LookupResolver;
  lookupCleanerReloader: LookupLabelsCleanerReloader;

  startup(): void;

  teardown(): void;

  cleanAndReload(): void;
};