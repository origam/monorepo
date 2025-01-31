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

import { IProperty } from "./types/IProperty";
import { getDataTable } from "model/selectors/DataView/getDataTable";
import { getDataView } from "model/selectors/DataView/getDataView";
import { getApi } from "model/selectors/getApi";
import { getMenuItemId } from "model/selectors/getMenuItemId";
import { getSessionId } from "model/selectors/getSessionId";
import { getDataStructureEntityId } from "model/selectors/DataView/getDataStructureEntityId";
import { getUserFilters } from "model/selectors/DataView/getUserFilters";
import { getUserFilterLookups } from "model/selectors/DataView/getUserFilterLookups";

export function*getAllLookupIds(property: IProperty): Generator {
  const dataView = getDataView(property);
  if (dataView.isLazyLoading) {
    return yield getAllValuesOfProp(property);
  } else {
    const dataTable = getDataTable(property);
    return yield dataTable.getAllValuesOfProp(property);
  }
}

async function getAllValuesOfProp(property: IProperty): Promise<Set<any>> {
  const api = getApi(property);
  const listValues = await api.getFilterListValues({
    MenuId: getMenuItemId(property),
    SessionFormIdentifier: getSessionId(property),
    DataStructureEntityId: getDataStructureEntityId(property),
    Property: property.id,
    Filter: property.column === "TagInput" || property.column === "Checklist"
      ? ""
      : getUserFilters({ctx: property, excludePropertyId: property.id}),
    FilterLookups: getUserFilterLookups(property),
  });
  return new Set(
    listValues
      .map(listValue => listValue)
      .filter(listValue => listValue)
  );
}
