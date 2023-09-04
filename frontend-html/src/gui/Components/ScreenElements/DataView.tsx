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

import { CDataViewHeader } from "gui/connections/CDataViewHeader";
import { inject, Observer, observer, Provider } from "mobx-react";
import { getIsDataViewOrFormScreenWorkingDelayed } from "model/selectors/DataView/getIsDataViewOrFormScreenWorking";
import React, { createContext, ReactNode, useState } from "react";
import { IDataView } from "../../../model/entities/types/IDataView";
import { getDataViewById } from "../../../model/selectors/DataView/getDataViewById";
import S from "./DataView.module.css";
import { DataViewLoading } from "./DataViewLoading";
import { scopeFor } from "dic/Container";
import { IDataViewBodyUI } from "modules/DataView/DataViewUI";
import { TreeView } from "./TreeView";
import { observable } from "mobx";
import { getIsDataViewOrFormScreenWorking } from "model/selectors/DataView/getIsDataViewOrFormScreenWorking.1";
import { EventHandler } from "utils/events";
import { isMobileLayoutActive } from "model/selectors/isMobileLayoutActive";
import { MobileDataViewHeader } from "gui/connections/MobileComponents/Grid/DataViewHeader";

export interface IDataViewHeaderExtensionItem {
  $iid: number;
  group: string;

  render(): ReactNode;
}

export class DataViewHeaderExtension {
  @observable items = new Map<number, IDataViewHeaderExtensionItem>();

  put(item: IDataViewHeaderExtensionItem) {
    this.items.set(item.$iid, item);
  }

  del(item: { $iid: number }) {
    this.items.delete(item.$iid);
  }

  render(group: string) {
    return (
      <Observer>
        {() => (
          <>
            {Array.from(this.items)
              .filter(([iid, item]) => item.group === group)
              .map(([iid, item]) => (
                <Observer key={iid}>{() => <>{item.render()}</>}</Observer>
              ))}
          </>
        )}
      </Observer>
    );
  }
}

export const CtxDataViewHeaderExtension = createContext<DataViewHeaderExtension>(
  new DataViewHeaderExtension()
);

interface IDataViewProps {
  id: string;
  modelInstanceId: string;
  height?: number;
  width?: number;
  isHeadless: boolean;
  dataView?: IDataView;
}

@inject(({formScreen}, {id}) => {
  const dataView = getDataViewById(formScreen, id);
  return {
    dataView,
  };
})
@observer
export class DataViewInner extends React.Component<IDataViewProps> {
  dataViewHeaderExtension = new DataViewHeaderExtension();

  getDataViewStyle() {
    if (this.props.height !== undefined || this.props.width !== undefined) {
      return {
        flexGrow: 0,
        minHeight: this.props.height,
        minWidth: this.props.width,
      };
    } else {
      return {
        flexGrow: 1,
        flexShrink: 0,
        // width: "100%",
        // height: "100%"
      };
    }
  }

  renderUiBodyWithHeader() {
    const $cont = scopeFor(this.props.dataView);
    const uiBody = $cont && $cont.resolve(IDataViewBodyUI);
    const isWorking = getIsDataViewOrFormScreenWorking(this.props.dataView);
    return (
      <>
        <div className={S.overlayContainer}>
          {isMobileLayoutActive(this.props.dataView)
            ? <MobileDataViewHeader isVisible={!this.props.isHeadless}/>
            : <CDataViewHeader isVisible={!this.props.isHeadless}/>
          }

          {isWorking && <DataViewLoading/>}
        </div>
        <div className={S.dataViewContentContainer}>{uiBody && uiBody.render()}</div>
      </>
    );
  }

  render() {
    if(!this.props.dataView){
      return null;
    }
    // TODO: Move styling to stylesheet
    const isWorkingDelayed = getIsDataViewOrFormScreenWorkingDelayed(this.props.dataView);

    return (
      <CtxDataViewHeaderExtension.Provider value={this.dataViewHeaderExtension}>
        <Provider dataView={this.props.dataView}>
          <div className={S.dataView} style={this.getDataViewStyle()} id={"dataView_" + this.props.modelInstanceId}>
            {this.props.dataView?.type === "TreePanel" ? (
              <TreeView dataView={this.props.dataView}/>
            ) : (
              this.renderUiBodyWithHeader()
            )}

            {isWorkingDelayed && <DataViewLoading/>}
          </div>
        </Provider>
      </CtxDataViewHeaderExtension.Provider>
    );
  }
}

export class DataViewContext {
  private tableKeyDownChannel = new EventHandler();

  subscribeTableKeyDownHandler(fn: (event: any) => void) {
    return this.tableKeyDownChannel.subscribe(fn);
  }

  handleTableKeyDown(event: any) {
    this.tableKeyDownChannel.trigger(event);
  }
}

export const CtxDataView = createContext<DataViewContext | undefined>(undefined);

export function DataView(props: IDataViewProps) {
  const [context] = useState(() => new DataViewContext());
  return (
    <CtxDataView.Provider value={context}>
      <DataViewInner {...props} />
    </CtxDataView.Provider>
  );
}
