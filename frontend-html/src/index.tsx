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


import axios from "axios";
import { flow } from "mobx";
import { getApi } from "model/selectors/getApi";
import { ensureLogin, userManager } from "oauth";
import React from "react";
import ReactDOM from "react-dom";
import { Root } from "Root";
import "./index.scss";
import { createApplication } from "./model/factories/createApplication";
import "./rootContainer";
import * as serviceWorker from "./serviceWorker";
import Cookie from "js-cookie";
import { translationsInit } from "./utils/translation";
import { getLocaleFromCookie, initLocaleCookie } from "utils/cookies";
import moment from "moment";
import 'moment/dist/locale/de';
import 'moment/dist/locale/de-at';
import 'moment/dist/locale/de-ch';
import 'moment/dist/locale/fr.js';
import 'moment/dist/locale/fr-ch.js';
import 'moment/dist/locale/fr-ca.js';
import 'moment/dist/locale/cs';
import { preventDoubleclickSelect } from "utils/mouse";
import { RootError } from "RootError";
// eslint-disable-next-line @typescript-eslint/no-unused-vars
import { ArrayPrototypes } from "@origam/utils"
import {
  isAddRecordShortcut,
  isDeleteRecordShortcut,
  isDuplicateRecordShortcut,
  isFilterRecordShortcut
} from "utils/keyShortcuts";

if (import.meta.env.DEV) {
  axios.defaults.timeout = 3600000;
  (window as any).ORIGAM_CLIENT_AXIOS_LIB = axios;
}

(window as any).ORIGAM_CUSTOM_CLIENT_BUILD = import.meta.env.VITE_REACT_APP_ORIGAM_CUSTOM_CLIENT_BUILD;
(window as any).ORIGAM_UI_PLUGINS = import.meta.env.VITE_REACT_APP_ORIGAM_UI_PLUGINS;
(window as any).ORIGAM_SERVER_PLUGINS = import.meta.env.VITE_REACT_APP_ORIGAM_SERVER_PLUGINS;

function disableAutoZoomingOnIPhone(){
  const safariDerivedBrowser = navigator.vendor && navigator.vendor.indexOf('Apple') > -1;
  if(safariDerivedBrowser){
    document
      .querySelector('meta[name="viewport"]')
      ?.setAttribute("content", "width=device-width, initial-scale=1, maximum-scale=1");
  }
}

function disableCollidingBrowserShortcuts() {
  const ignoreShortcuts = (event: KeyboardEvent) => {
    if (
      isAddRecordShortcut(event) ||
      isDuplicateRecordShortcut(event) ||
      isDeleteRecordShortcut(event) ||
      isFilterRecordShortcut(event)
    ) {
      event.preventDefault();
    }
  };

  window.addEventListener("keydown", ignoreShortcuts);
}

async function main() {
  disableAutoZoomingOnIPhone();
  preventDoubleclickSelect();
  disableCollidingBrowserShortcuts();
  const locationHash = window.location.hash;
  const TOKEN_OVR_HASH = "#origamAuthTokenOverride=";
  if (locationHash.startsWith(TOKEN_OVR_HASH)) {
    sessionStorage.setItem("origamAuthTokenOverride", locationHash.replace(TOKEN_OVR_HASH, ""));
    window.location.hash = "";
  }
  const BACKEND_OVR_HASH = "#origamBackendOverride=";
  if (locationHash.startsWith(BACKEND_OVR_HASH)) {
    const backendUrl = locationHash.replace(BACKEND_OVR_HASH, "");
    const newUrl = backendUrl + `#origamBackendOverrideReturn=${window.location.origin}`;
    Cookie.set("backendUrl", backendUrl);
    window.location.assign(newUrl);
    return;
  }

  const BACKEND_OVR_RETURN_HASH = "#origamBackendOverrideReturn=";
  if (locationHash.startsWith(BACKEND_OVR_RETURN_HASH)) {
    window.sessionStorage.setItem(
      "teleportAfterLogin",
      locationHash.replace(BACKEND_OVR_RETURN_HASH, "")
    );
    window.location.hash = "";
    for (let k of Object.keys(window.sessionStorage)) {
      if (k.startsWith("oidc.user")) {
        window.sessionStorage.removeItem(k);
      }
    }
  }
  let user;
  try {
    user = await ensureLogin();
  } catch (e) {
    const application = createApplication();
    await initLocaleCookie(application);
    await translationsInit(application);
    ReactDOM.render(<RootError error={e}/>, document.getElementById("root"));
    return;
  }
  if (user) {
    if (window.sessionStorage.getItem("teleportAfterLogin")) {
      window.sessionStorage.removeItem("teleportAfterLogin");
      return;
    }
    const application = createApplication();
    getApi(application).setAccessToken(user.access_token);
    sessionStorage.setItem("origamAuthToken", user.access_token);
    userManager.events.addUserLoaded((user: any) => {
      getApi(application).setAccessToken(user.access_token);
      sessionStorage.setItem("origamAuthToken", user.access_token);
    });
    flow(application.run.bind(application))();

    await initLocaleCookie(application);
    const locale = await getLocaleFromCookie();
    document.documentElement.lang = locale;
    moment.locale(locale);

    await translationsInit(application);

    ReactDOM.render(<Root application={application}/>, document.getElementById("root"));
  }
}

main();

// If you want your app to work offline and load faster, you can change
// unregister() to register() below. Note this comes with some pitfalls.
// Learn more about service workers: https://bit.ly/CRA-PWA
serviceWorker.unregister();



