import {
  ILocalization,
  ILocalizer,
  IPlugin,
  ISectionPlugin,
  ISectionPluginData,
} from "@origam/plugins";
import { action, observable } from "mobx";
import { registerPlugin } from "plugins/tools/PluginLibrary";
import { UploadArea } from "./UploadArea";
import { IDataView } from "model/entities/types/IDataView";
import { Observer } from "mobx-react";

export class TaldisSupplierInvoicePlugin implements ISectionPlugin {
  $type_ISectionPlugin = 1;
  @observable isInitialized = false;
  xmlAttributes: any;

  getScreenParameters: (() => { [key: string]: string }) | undefined;

  getComponent(
    data: ISectionPluginData,
    createLocalizer: (localizations: ILocalization[]) => ILocalizer
  ): JSX.Element {
    return (
      <Observer>
        {() =>
          this.isInitialized ? (
            <UploadArea
              dataView={(data.dataView as any).dataView as IDataView}
              pluginProperties={this.xmlAttributes}
            />
          ) : null
        }
      </Observer>
    );
  }

  @action.bound
  initialize(xmlAttributes: { [key: string]: string }): void {
    this.isInitialized = true;
    this.xmlAttributes = xmlAttributes;
  }

  onSessionRefreshed(): void {
    //throw new Error("Method not implemented.");
    // TODO: implement
    console.warn("🍗 What shall I do now?");
  }

  id: string = "";
}

;
