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

import { TagInputEditor } from "gui/Components/ScreenElements/Editors/TagInputEditor";
import { TextEditor } from "gui/Components/ScreenElements/Editors/TextEditor";
import { inject, observer, Provider } from "mobx-react";
import { onFieldChange } from "model/actions-ui/DataView/TableView/onFieldChange";
import { onFieldKeyDown } from "model/actions-ui/DataView/TableView/onFieldKeyDown";
import { getRowStateForegroundColor } from "model/selectors/RowState/getRowStateForegroundColor";
import { getSelectedRowId } from "model/selectors/TablePanelView/getSelectedRowId";
import React from "react";
import { onFieldBlur } from "model/actions-ui/DataView/TableView/onFieldBlur";
import { IProperty } from "model/entities/types/IProperty";
import { getSelectedRow } from "model/selectors/DataView/getSelectedRow";
import { getCellValue } from "model/selectors/TablePanelView/getCellValue";
import { getSelectedProperty } from "model/selectors/TablePanelView/getSelectedProperty";
import { BoolEditor } from "gui/Components/ScreenElements/Editors/BoolEditor";
import { DateTimeEditor } from "gui/Components/ScreenElements/Editors/DateTimeEditor/DateTimeEditor";
import { NumberEditor } from "gui/Components/ScreenElements/Editors/NumberEditor";
import { BlobEditor } from "gui/Components/ScreenElements/Editors/BlobEditor";
import { XmlBuildDropdownEditor } from "modules/Editors/DropdownEditor/DropdownEditor";
import { getDataView } from "model/selectors/DataView/getDataView";
import uiActions from "../../../../model/actions-ui-tree";
import { isReadOnly } from "model/selectors/RowState/isReadOnly";
import { getTablePanelView } from "model/selectors/TablePanelView/getTablePanelView";
import { rowHeight } from "gui/Components/ScreenElements/Table/TableRendering/cells/cellsCommon";
import { shadeHexColor } from "utils/colorUtils";
import { getRowStateRowBgColor } from "model/selectors/RowState/getRowStateRowBgColor";
import ColorEditor from "gui/Components/ScreenElements/Editors/ColorEditor";
import { flashColor2htmlColor, htmlColor2FlashColor } from "utils/flashColorFormat";
import { getGridFocusManager } from "model/entities/GridFocusManager";
import { CellAlignment } from "gui/Components/ScreenElements/Table/TableRendering/cells/cellAlignment";

@inject(({tablePanelView}) => {
  const row = getSelectedRow(tablePanelView)!;
  const property = getSelectedProperty(tablePanelView)!;
  const actualProperty =
    property.column === "Polymorph" ? property.getPolymophicProperty(row) : property;
  return {
    property: actualProperty,
    getCellValue: () => getCellValue(tablePanelView, row, actualProperty),
    onChange: async (event: any, value: any) =>
      await onFieldChange(tablePanelView)({
        event: event,
        row: row,
        property: actualProperty,
        value: value,
      }),
    onEditorBlur: async () => {
      await onFieldBlur(tablePanelView)();
      const gridFocusManager = getGridFocusManager(tablePanelView);
      gridFocusManager.activeEditor = undefined;
      gridFocusManager.editorBlur = undefined;
    },
    onEditorKeyDown: (event: any) => {
      event.persist();
      onFieldKeyDown(tablePanelView)(event);
    },
  };
})
@observer
export class TableViewEditor extends React.Component<{
  property?: IProperty;
  getCellValue?: () => any;
  onChange?: (event: any, value: any) => Promise<void>;
  onEditorBlur?: () => Promise<void>;
  onEditorKeyDown?: (event: any) => void;
}> {

  componentDidMount() {
    const focusManager = getGridFocusManager(this.props.property);
    focusManager.focusEditor();
  }

  getEditor() {
    const rowId = getSelectedRowId(this.props.property);
    const foregroundColor = getRowStateForegroundColor(this.props.property, rowId || "");
    const dataView = getDataView(this.props.property);
    const readOnly =
      isReadOnly(this.props.property!, rowId) ||
      (dataView.orderProperty !== undefined &&
        this.props.property?.name === dataView.orderProperty.name);

    const customBackgroundColor = getRowStateRowBgColor(dataView, rowId);
    const backgroundColor = readOnly
      ? shadeHexColor(customBackgroundColor, -0.1)
      : customBackgroundColor;

    const isFirsColumn = getTablePanelView(dataView)?.firstColumn === this.props.property;
    const gridFocusManager = getGridFocusManager(this.props.property);
    switch (this.props.property!.column) {
      case "Number":
        return (
          <NumberEditor
            value={this.props.getCellValue!()}
            isReadOnly={readOnly}
            property={this.props.property}
            isPassword={this.props.property!.isPassword}
            maxLength={this.props.property?.maxLength}
            backgroundColor={backgroundColor}
            foregroundColor={foregroundColor}
            customNumberFormat={this.props.property!.customNumericFormat}
            onChange={this.props.onChange}
            onKeyDown={this.props.onEditorKeyDown}
            onClick={undefined}
            onDoubleClick={(event) => this.onDoubleClick(event)}
            onEditorBlur={this.props.onEditorBlur}
            customStyle={resolveCellAlignment(this.props.property?.style, isFirsColumn, "Number")}
            subscribeToFocusManager={(editor, onBlur) =>{
                gridFocusManager.activeEditor = editor
                gridFocusManager.editorBlur = onBlur;
              }
            }
          />
        );
      case "Text":
        return (
          <TextEditor
            id={"editor_" + this.props.property?.modelInstanceId}
            value={this.props.getCellValue!()}
            isReadOnly={readOnly}
            isPassword={this.props.property!.isPassword}
            isAllowTab={false}
            backgroundColor={backgroundColor}
            foregroundColor={foregroundColor}
            maxLength={this.props.property?.maxLength}
            onChange={this.props.onChange}
            onKeyDown={this.props.onEditorKeyDown}
            onClick={undefined}
            wrapText={false}
            customStyle={resolveCellAlignment(this.props.property?.style, isFirsColumn, "Text")}
            onDoubleClick={(event) => this.onDoubleClick(event)}
            onEditorBlur={this.props.onEditorBlur}
            isRichText={false}
            isMultiline={this.props.property!.multiline}
            subscribeToFocusManager={(editor) =>
            {
              gridFocusManager.activeEditor = editor
              gridFocusManager.editorBlur = this.props.onEditorBlur;
            }}
          />
        );
      case "Date":
        return (
          <DateTimeEditor
            value={this.props.getCellValue!()}
            outputFormat={this.props.property!.formatterPattern}
            outputFormatToShow={this.props.property!.modelFormatterPattern}
            isReadOnly={readOnly}
            backgroundColor={backgroundColor}
            foregroundColor={foregroundColor}
            onChange={this.props.onChange}
            onClick={undefined}
            onDoubleClick={(event) => this.onDoubleClick(event)}
            onEditorBlur={this.props.onEditorBlur}
            onKeyDown={this.props.onEditorKeyDown}
            subscribeToFocusManager={(editor, onBlur) =>
            {
              gridFocusManager.activeEditor = editor;
              gridFocusManager.editorBlur = onBlur;
            }}
          />
        );
      case "CheckBox":
        return (
          <BoolEditor
            value={this.props.getCellValue!()}
            isReadOnly={readOnly}
            readOnlyNoGrey={true}
            onChange={this.props.onChange}
            onClick={undefined}
            onKeyDown={this.props.onEditorKeyDown}
            subscribeToFocusManager={(editor) =>
              gridFocusManager.activeEditor = editor
            }
          />
        );
      case "ComboBox":
        return (
          <XmlBuildDropdownEditor
            key={this.props.property!.xmlNode.$iid}
            xmlNode={this.props.property!.xmlNode}
            onDoubleClick={(event) => this.onDoubleClick(event)}
            isReadOnly={readOnly}
            foregroundColor={foregroundColor}
            backgroundColor={backgroundColor}
            autoSort={this.props.property!.autoSort}
            onKeyDown={this.props.onEditorKeyDown}
            subscribeToFocusManager={(editor) =>
              gridFocusManager.activeEditor = editor
            }
          />
        );
      case "Checklist":
        return "";
      case "Color":
        return (
          <ColorEditor
            value={flashColor2htmlColor(this.props.getCellValue!()) || null}
            onChange={(value) => this.props.onChange?.(undefined, htmlColor2FlashColor(value))}
            onBlur={() => this.props.onEditorBlur?.()}
            onKeyDown={this.props.onEditorKeyDown}
            isReadOnly={readOnly}
            subscribeToFocusManager={(editor) =>
              gridFocusManager.activeEditor = editor
            }
          />
        );
      case "TagInput":
        return (
          <div style={{height: rowHeight * 5 + "px", backgroundColor: "white"}}>
            <XmlBuildDropdownEditor
              key={this.props.property!.xmlNode.$iid}
              xmlNode={this.props.property!.xmlNode}
              isReadOnly={readOnly}
              autoSort={this.props.property!.autoSort}
              subscribeToFocusManager={(editor) =>
                gridFocusManager.activeEditor = editor
              }
              tagEditor={
                <TagInputEditor
                  value={this.props.getCellValue!()}
                  isReadOnly={readOnly}
                  backgroundColor={backgroundColor}
                  foregroundColor={foregroundColor}
                  onChange={this.props.onChange}
                  onKeyDown={this.props.onEditorKeyDown}
                  onDoubleClick={(event) => this.onDoubleClick(event)}
                  onEditorBlur={this.props.onEditorBlur}
                />
              }
            />
          </div>
        );
      case "Blob":
        return (
          <BlobEditor
            isReadOnly={readOnly}
            value={this.props.getCellValue!()}
            canUpload={true}
            onChange={this.props.onChange}
            onEditorBlur={this.props.onEditorBlur}
            subscribeToFocusManager={(editor) =>
              gridFocusManager.activeEditor = editor
            }
          />
        );
      case "Polymorph":
        console.warn(`Type of polymorphic column was not determined, no editor was rendered`); // eslint-disable-line no-console
        return "";
      default:
        console.warn(`Unknown column type "${this.props.property!.column}", no editor was rendered`) // eslint-disable-line no-console
        return "";
    }
  }

  onDoubleClick(event: any) {
    getTablePanelView(this.props.property).setEditing(false);
    const dataView = getDataView(this.props.property);
    if (!dataView.firstEnabledDefaultAction) {
      return;
    }
    uiActions.actions.onActionClick(dataView.firstEnabledDefaultAction)(
      event,
      dataView.firstEnabledDefaultAction
    );
  }

  render() {
    return <Provider property={this.props.property}>{this.getEditor()}</Provider>;
  }
}

// Makes sure the editor alignment will be the same as the table cell alignment.
// Needed on columns where the alignment can be set in the model.
function resolveCellAlignment(customStyle: { [p: string]: string } | undefined, isFirsColumn: boolean, type: string){
  let cellAlignment = new CellAlignment(isFirsColumn, type, customStyle);
  const style = customStyle ?Object.assign({},customStyle) :{};
  style["paddingRight"] = cellAlignment.paddingRight - 1 + "px";
  style["paddingLeft"] = cellAlignment.paddingLeft + "px";
  style["textAlign"] = cellAlignment.alignment;
  return style;
}