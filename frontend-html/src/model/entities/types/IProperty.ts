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

import { ICaptionPosition } from "./ICaptionPosition";
import { IPropertyColumn } from "./IPropertyColumn";
import { ILookup } from "./ILookup";
import { ILookupIndividualEngine } from "../Property";

export enum IDockType {
  Dock = "Dock",
  Fill = "Fill",
}

export interface IPropertyData {
  id: string;
  tabIndex: string | undefined;
  modelInstanceId: string;
  name: string;
  readOnly: boolean;
  x: number;
  y: number;
  width: number;
  height: number;
  captionLength: number;
  captionPosition?: ICaptionPosition;
  entity: string;
  column: IPropertyColumn;
  dock?: IDockType;
  multiline: boolean;
  isAllowTab?: boolean;
  alwaysHidden: boolean;
  isPassword: boolean;
  isRichText: boolean;
  maxLength: number;
  gridColumnWidth: number;
  columnWidth: number;
  formatterPattern: string;
  modelFormatterPattern: string;
  customNumericFormat?: string;
  isAggregatedColumn: boolean;
  isLookupColumn: boolean;
  autoSort: boolean;
  toolTip: string;
  suppressEmptyColumns: boolean;
  supportsServerSideSorting: boolean;

  controlPropertyValue?: string;
  controlPropertyId?: string;
  parameters?: any;
  allowReturnToForm?: boolean;
  isTree?: boolean;
  style: any;
  identifier?: string;
  lookup?: ILookup;
  lookupId?: string;
  xmlNode: any;
  fieldType: string;
}

export interface IProperty extends IPropertyData {
  $type_IProperty: 1;

  dataSourceIndex: number;
  dataIndex: number;
  isLookup: boolean;
  alwaysHidden: boolean;
  lookupEngine?: ILookupIndividualEngine;

  childProperties: IProperty[];
  linkToMenuId?: string;
  linkDependsOnValue: boolean;
  isLink: boolean;
  nameOverride: string | null | undefined;
  isFormField: boolean;


  getPolymophicProperty(row: any[]): IProperty;

  setColumnWidth(width: number): void;

  stop(): void;

  parent?: any;
}

export const isIProperty = (o: any): o is IProperty => o.$type_IProperty;
