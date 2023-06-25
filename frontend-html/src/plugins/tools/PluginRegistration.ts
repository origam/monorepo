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

import { registerPlugin } from "plugins/tools/PluginLibrary";
import { AuditPlugin } from "@origam/plugin-audit";
import { FilterPlugin } from "@origam/plugin-filter";
import { RadarChartPlugin } from "@origam/plugin-chart";

export async function registerPlugins() {
  registerPlugin("FilterPlugin", () => new FilterPlugin());
  registerPlugin("AuditPlugin", () => new AuditPlugin());
  registerPlugin("RadarChartPlugin", () => new RadarChartPlugin());

  await autoimportPlugins();
} 

async function autoimportPlugins() {
  const imports = import.meta.glob('/src/plugins/autoimport/*/bootstrap.tsx')
  for(let loadPluginModule of Object.values(imports)) {
    const pluginModule: any = await loadPluginModule()
    await pluginModule.bootstrap();
  }
}