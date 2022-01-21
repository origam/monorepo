import { DropdownColumnDrivers, DropdownDataTable } from "modules/Editors/DropdownEditor/DropdownTableModel";
import { IDriverState } from "modules/Editors/DropdownEditor/Cells/IDriverState";
import { findStopping } from "xmlInterpreters/xmlUtils";
import { getMomentFormat } from "xmlInterpreters/getMomentFormat";
import { TextCellDriver } from "modules/Editors/DropdownEditor/Cells/TextCellDriver";
import { NumberCellDriver } from "modules/Editors/DropdownEditor/Cells/NumberCellDriver";
import { BooleanCellDriver } from "modules/Editors/DropdownEditor/Cells/BooleanCellDriver";
import { DateCellDriver } from "modules/Editors/DropdownEditor/Cells/DateCellDriver";
import { DefaultHeaderCellDriver } from "modules/Editors/DropdownEditor/Cells/HeaderCell";

export class DropdownEditorSetup {
  constructor(
    public propertyId: string,
    public lookupId: string,
    public columnNames: string[],
    public visibleColumnNames: string[],
    public columnNameToIndex: Map<string, number>,
    public showUniqueValues: boolean,
    public identifier: string,
    public identifierIndex: number,
    public parameters: { [key: string]: any },
    public dropdownType: string,
    public cached: boolean,
    public searchByFirstColumnOnly: boolean,
    public columnDrivers: DropdownColumnDrivers,
    public isLink?: boolean,
  ) {
  }
}

export function DropdownEditorSetupFromXml(
  xmlNode: any,
  dropdownEditorDataTable: DropdownDataTable,
  dropdownEditorBehavior: IDriverState
): DropdownEditorSetup {
  const rat = xmlNode.attributes;
  const lookupId = rat.LookupId;
  const propertyId = rat.Id;
  const showUniqueValues = rat.DropDownShowUniqueValues === "true";
  const identifier = rat.Identifier;
  let identifierIndex = 0;
  const dropdownType = rat.DropDownType;
  const cached = rat.Cached === "true";
  const searchByFirstColumnOnly = rat.SearchByFirstColumnOnly === "true";

  const columnNames: string[] = [identifier];
  const visibleColumnNames: string[] = [];
  const columnNameToIndex = new Map<string, number>([[identifier, identifierIndex]]);
  let index = 0;
  const drivers = new DropdownColumnDrivers();
  if (rat.SuppressEmptyColumns === "true") {
    drivers.driversFilter = (driver) => {
      return dropdownEditorDataTable.columnIdsWithNoData.indexOf(driver.columnId) < 0;
    };
  }
  for (let propertyXml of findStopping(xmlNode, (n) => n.name === "Property")) {
    index++;
    const attributes = propertyXml.attributes;
    const id = attributes.Id;
    columnNames.push(id);
    columnNameToIndex.set(id, index);

    const formatterPattern = attributes.FormatterPattern
      ? getMomentFormat(propertyXml)
      : "";
    visibleColumnNames.push(id);
    const name = attributes.Name;
    const column = attributes.Column;

    let bodyCellDriver;
    switch (column) {
      case "Text":
        bodyCellDriver = new TextCellDriver(
          index,
          dropdownEditorDataTable,
          dropdownEditorBehavior
        );
        break;
      case "Number":
        bodyCellDriver = new NumberCellDriver(
          index,
          dropdownEditorDataTable,
          dropdownEditorBehavior
        );
        break;
      case "CheckBox":
        bodyCellDriver = new BooleanCellDriver(
          index,
          dropdownEditorDataTable,
          dropdownEditorBehavior
        );
        break;
      case "Date":
        bodyCellDriver = new DateCellDriver(
          index,
          dropdownEditorDataTable,
          dropdownEditorBehavior,
          formatterPattern
        );
        break;
      default:
        throw new Error("Unknown column type " + column);
    }

    drivers.allDrivers.push({
      columnId: id,
      headerCellDriver: new DefaultHeaderCellDriver(name),
      bodyCellDriver,
    });
  }
  const parameters: { [k: string]: any } = {};

  for (let ddp of findStopping(xmlNode, (n) => n.name === "ComboBoxParameterMapping")) {
    const pat = ddp.attributes;
    parameters[pat.ParameterName] = pat.FieldName;
  }
  return new DropdownEditorSetup(
    propertyId,
    lookupId,
    columnNames,
    visibleColumnNames,
    columnNameToIndex,
    showUniqueValues,
    identifier,
    identifierIndex,
    parameters,
    dropdownType,
    cached,
    searchByFirstColumnOnly,
    drivers,
  );
}

