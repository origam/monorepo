import { useEffect, useRef, useState } from "react";
import style from "./UploadArea.module.scss";
import { observer, inject } from "mobx-react";
import React from "react";
import { IApi } from "model/entities/types/IApi";
import { IProperty } from "model/entities/types/IProperty";
import { getApi } from "model/selectors/getApi";
import { processCRUDResult } from "model/actions/DataLoading/processCRUDResult";
import { getMenuItemId } from "model/selectors/getMenuItemId";
import { getSessionId } from "model/selectors/getSessionId";
import { getDataStructureEntityId } from "model/selectors/DataView/getDataStructureEntityId";
import { IDataView } from "model/entities/types/IDataView";
import { handleError } from "model/actions/handleError";
import { getSelectedRowId } from "model/selectors/TablePanelView/getSelectedRowId";
import { getEntity } from "model/selectors/DataView/getEntity";
import { getProperty } from "model/selectors/DataView/getProperty";
import { getDataViewPropertyById } from "model/selectors/DataView/getDataViewPropertyById";
import { flow } from "mobx";
import { CancellablePromise } from "mobx/lib/internal";
import { IAction } from "model/entities/types/IAction";
import actions from "model/actions-ui-tree/actions";
import { getDataTable } from "model/selectors/DataView/getDataTable";
import { getFormScreen } from "model/selectors/FormScreen/getFormScreen";

type PluginProperties = {
  InvoiceFilenameMember: string;
  UIActionSourcePanelId: string;
  ParseUIActionId: string;
  CreditorMember: string;
  BlobMember: string;
  BlobLookupId: string;
}

@inject(
  (
    {}: {},
    {
      dataView,
      pluginProperties,
    }: {
      dataView: IDataView;
      pluginProperties: PluginProperties;
    }
  ) => {
    const dataTable = getDataTable(dataView);
    const valuePropertyObject = getDataViewPropertyById(
      dataView,
      pluginProperties.InvoiceFilenameMember
    );

    const formScreen = getFormScreen(dataView);
    const uiActionSourceDataView = formScreen.dataViews.find(
      (item) => item.modelInstanceId === pluginProperties.UIActionSourcePanelId
    );
    const parseActionObject = uiActionSourceDataView!.actions.find(
      (item) => item.id === pluginProperties.ParseUIActionId
    );

    const row = dataView.tableRows[0] as any[];
    const filenameValue =
      row && valuePropertyObject
        ? dataTable.getCellValue(row, valuePropertyObject)
        : undefined;
    const creditorPropertyObject = getDataViewPropertyById(
      dataView,
      pluginProperties.CreditorMember
    );
    const creditorValue =
      row && creditorPropertyObject
        ? dataTable.getCellValue(row, creditorPropertyObject)
        : undefined;
    return {
      api: getApi(dataView),
      processCRUDResult: (result: any) =>
        flow(processCRUDResult)(dataView, result),
      onActionClick: actions.onActionClick(dataView),
      handleError: flow(handleError(dataView)),
      DataStructureEntityId: getDataStructureEntityId(dataView),
      Property: valuePropertyObject!.name,
      RowId: getSelectedRowId(dataView),
      MenuId: getMenuItemId(dataView),
      Entity: getEntity(dataView),
      SessionFormIdentifier: getSessionId(dataView),
      parameters: valuePropertyObject!.parameters,
      parseActionObject,
      creditorValue,
      filenameValue,
      BlobMember: pluginProperties.BlobMember,
      BlobLookupId: pluginProperties.BlobLookupId,
    };
  }
)
@observer
export class UploadArea extends React.Component<{
  api?: IApi;
  dataView: IDataView;
  Entity?: string;
  SessionFormIdentifier?: string;
  DataStructureEntityId?: string;
  RowId?: string;
  MenuId?: string;
  Property?: string;
  BlobMember?: string;
  BlobLookupId?: string;
  parameters?: any;
  parseActionObject?: IAction;
  creditorValue?: any;
  filenameValue?: any;
  pluginProperties: PluginProperties;
  handleError?: (error: any) => CancellablePromise<void>;
  processCRUDResult?: (result: any) => CancellablePromise<any>;
  onActionClick?: (
    event: any,
    action: IAction,
    beforeHandleError?: () => void
  ) => Promise<any>;
}> {
  render() {
    return <UploadAreaInner {...this.props as Required<typeof this.props>} />;
  }
}

const UploadAreaInner: React.FC<{
  api: IApi;
  dataView: IDataView;
  SessionFormIdentifier: string;
  DataStructureEntityId: string;
  Entity: string;
  RowId: string;
  MenuId: string;
  Property: string;
  BlobMember: string;
  BlobLookupId: string;
  parameters: any;
  parseActionObject: IAction;
  creditorValue: any;
  filenameValue: any;
  handleError: (error: any) => CancellablePromise<void>;
  processCRUDResult: (result: any) => CancellablePromise<any>;
  onActionClick: (
    event: any,
    action: IAction,
    beforeHandleError?: () => void
  ) => Promise<any>;
}> = (props) => {
  const hintTimerHandle = useRef<any>();
  const fileUploadInput = useRef<any>();
  const [iframeUrl, setIframeUrl] = useState<string | null>("");
  const [hint, setHint] = useState<null | string>(null);
  const [progress, setProgress] = useState<{
    speed: number;
    progress: number;
    activity: string;
  }>({ speed: 0, progress: 0, activity: "" });
  const hintOnlyOneFile = () => {
    clearTimeout(hintTimerHandle.current);
    hintTimerHandle.current = setTimeout(() => {
      hideHint();
    }, 3000);
    setHint("You need to provide exactly one file.");
  };
  const hideHint = () => {
    clearTimeout(hintTimerHandle.current);
    setHint(null);
  };
  const handleDrop = (arg: any) => {
    arg.persist();
    arg.preventDefault();
    if (progress.activity || hint) return;
    processFileList(arg.dataTransfer.files);
  };
  const handleDragOver = (arg: any) => {
    arg.persist();
    arg.preventDefault();
    if (arg.dataTransfer.items.length !== 1) {
      hintOnlyOneFile();
    } else {
      hideHint();
    }
  };
  const handleClick = () => {
    fileUploadInput.current?.click();
  };
  const handleFileUploadChange = (arg: any) => {
    processFileList(arg.target.files);
  };
  const processFileList = (files: File[]) => {
    if (files.length !== 1) {
      hintOnlyOneFile();
    } else {
      hideHint();
    }
    upload(files[0]);
  };

  const upload = async (file: File) => {
    try {
      setProgress({
        speed: 0,
        progress: 0,
        activity: "Preparing...",
      });
      const token = await props.api!.getUploadToken({
        SessionFormIdentifier: props.SessionFormIdentifier,
        MenuId: props.MenuId,
        DataStructureEntityId: props.DataStructureEntityId,
        Entity: props.Entity,
        RowId: props.RowId,
        Property: props.Property,
        FileName: file.name,
        parameters: { BlobMember: props.BlobMember },
        DateCreated: "2010-01-01",
        DateLastModified: "2010-01-01",
      });
      let lastTime: number | undefined;
      let lastSize: number = 0;
      setProgress({
        speed: 0,
        progress: 0,
        activity: "Uploading...",
      });
      await props.api!.putBlob(
        {
          uploadToken: token,
          fileName: file.name,
          file,
        },
        (event: any) => {
          let progressValue = event.loaded / event.total;
          let speedValue = 0;
          if (lastTime !== undefined) {
            const speedValue =
              ((event.loaded - lastSize) / (event.timeStamp - lastTime)) * 1000;
          }
          lastTime = event.timeStamp;
          lastSize = event.loaded;
          setProgress({
            speed: speedValue,
            progress: progressValue,
            activity: "Uploading...",
          });
        }
      );
      setProgress({
        speed: 0,
        progress: 0,
        activity: "Parsing...",
      });
      const crudResult = await props.api!.changes({
        SessionFormIdentifier: props.SessionFormIdentifier,
        Entity: props.Entity,
        RowId: props.RowId,
      });
      await props.processCRUDResult!(crudResult);
      await props.onActionClick(null, props.parseActionObject, () => {
        setProgress({
          speed: 0,
          progress: 0,
          activity: "",
        });
      });
    } catch (e) {
      setProgress({
        speed: 0,
        progress: 0,
        activity: "",
      });
      await props.handleError(e);
    } finally {
      setProgress({
        speed: 0,
        progress: 0,
        activity: "",
      });
    }
  };

  const getBlobUrl = async () => {
    try {
      setProgress({
        speed: 0,
        progress: 0,
        activity: "Getting preview...",
      });
      const token = await props.api!.getDownloadToken({
        SessionFormIdentifier: props.SessionFormIdentifier!,
        MenuId: props.MenuId,
        DataStructureEntityId: props.DataStructureEntityId!,
        Entity: props.Entity!,
        RowId: props.RowId!,
        Property: props.Property!,
        FileName: "name of file",
        parameters: {
          BlobMember: props.BlobMember,
          BlobLookupId: props.BlobLookupId,
        },
        isPreview: true,
      });
      const blobUrl = props.api!.getBlobUrl({ downloadToken: token });
      return blobUrl;
    } finally {
      setProgress({
        speed: 0,
        progress: 0,
        activity: "",
      });
    }
  };

  let isValidProcess = true;
  useEffect(() => {
    if (props.filenameValue && props.creditorValue) {
      setIframeUrl("about:blank");
      getBlobUrl()
        .then((result) => {
          if (isValidProcess) {
            setIframeUrl(result);
          } else {
          }
        })
        .catch((error) => {
          setIframeUrl(null);
          handleError(error);
        });
    } else {
      setIframeUrl(null);
    }
    return () => {
      isValidProcess = false;
    };
  }, [props.filenameValue, props.creditorValue]);

  if (!iframeUrl)
    return (
      <div className={style.UploadAreaWrapper}>
        <div
          className={style.UploadArea}
          onDrop={handleDrop}
          onDragOver={handleDragOver}
          onClick={handleClick}
        >
          {!hint && !progress.activity && (
            <>
              <div className={style.UploadArea__message}>
                <i className="far fa-file-pdf" />
                Drag & Drop hierher
              </div>
              <input
                type="file"
                onChange={handleFileUploadChange}
                ref={fileUploadInput}
                className={style.UploadArea__upload}
              />
            </>
          )}
          {hint && !progress.activity && (
            <>
              <div className={style.UploadArea__hint}>
                <i className="fas fa-info-circle" />
                {hint}
              </div>
            </>
          )}
          {!hint && progress.activity && (
            <>
              <div className={style.UploadArea__hint}>
                <i className="fas fa-cog fa-spin" />
                {progress.activity}{" "}
                {progress.progress ? (
                  <>({(progress.progress * 100).toFixed(1)}%)</>
                ) : null}
              </div>
            </>
          )}
        </div>
      </div>
    );
  else return <iframe className={style.PDFViewer} src={iframeUrl} />;
};
