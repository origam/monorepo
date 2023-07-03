import { registerPlugin } from "plugins/tools/PluginLibrary";
import { TaldisSupplierInvoicePlugin } from "./taldisSupplierInvoiceBootstrap";

export const bootstrap = () => {
  registerPlugin("TaldisSupplierInvoicePlugin", () => {
    return new TaldisSupplierInvoicePlugin();
  });
}