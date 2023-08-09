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

// TODO: Extract types so that model layer does not depend on view layer?

import { IFilter } from "./IFilter";

export interface IFilterConfigurationData {
}

export interface IFilterConfiguration extends IFilterConfigurationData {
  $type_IFilterConfigurationData: 1;

  isFilterControlsDisplayed: boolean;
  activeFilters: IFilter[];

  filteringFunction(ignorePropertyId?: string): (row: any[], forceRowId?: string) => boolean;

  registerFilteringOnOffHandler(handler: (filteringOn: boolean) => void): void;

  getSettingByPropertyId(propertyId: string): IFilter | undefined;

  setFilter(term: IFilter): void;

  setFilters(filters: IFilter[]): void;

  clearFilters(): void;

  onFilterDisplayClick(event: any): void;

  parent?: any;
}
