﻿#region license
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
#endregion

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CSharpFunctionalExtensions;
using IdentityServer4;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using NPOI.SS.Formula.Functions;
using NPOI.SS.UserModel;
using Origam.DA;
using Origam.Excel;
using Origam.Server;
using Origam.Server.Model.Excel;
using Origam.Server.Model.UIService;

namespace Origam.Server.Controller;

[Authorize(IdentityServerConstants.LocalApi.PolicyName)]
[ApiController]
[Route("internalApi/[controller]")]
public class ExcelExportController: AbstractController
{
        
    public ExcelExportController(ILogger<AbstractController> log,
        SessionObjects sessionObjects) : base(log, sessionObjects)
    {
        }

    [HttpPost("[action]")]
    public IActionResult GetFile(
        [FromBody][Required]ExcelExportInput input)
    {
            return RunWithErrorHandler(() =>
            {
                SessionStore sessionStore = sessionObjects.SessionManager
                    .GetSession(new Guid(input.SessionFormIdentifier.ToString()));
                
                bool isLazyLoaded =
                    sessionStore.IsLazyLoadedEntity(input.Entity);
                if (isLazyLoaded && input.LazyLoadedEntityInput == null)
                {
                    return BadRequest(
                        "Export from lazy loaded entities requires " +
                        nameof(input.LazyLoadedEntityInput));
                }

                if (!isLazyLoaded && (input.RowIds == null || input.RowIds.Count == 0 ))
                {
                    return BadRequest(
                        "Export from non lazy loaded entities requires "+nameof(input.RowIds));
                }
                
                var entityExportInfo = new EntityExportInfo
                {
                    Entity = input.Entity,
                    Fields = input.Fields,
                    RowIds = input.RowIds,
                    SessionFormIdentifier = input.SessionFormIdentifier.ToString(),
                    Store = sessionStore,
                    LazyLoadedEntityInput = input.LazyLoadedEntityInput
                };

                return GetExcelFile(entityExportInfo);
            });
        }
        
    private IActionResult GetExcelFile(EntityExportInfo entityExportInfo)
    {
            return RunWithErrorHandler(() =>
            {
                var excelEntityExporter = new ExcelEntityExporter();
                var  workbookResult = FillWorkbook(entityExportInfo, excelEntityExporter);
                if (workbookResult.IsFailure)
                {
                    return workbookResult.Error;
                }
                if (excelEntityExporter.ExportFormat == ExcelFormat.XLS)
                {
                    Response.ContentType = "application/vnd.ms-excel";
                    Response.Headers.Add(
                        "content-disposition", "attachment; filename=export.xls");
                }
                else
                {
                    Response.ContentType
                        = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    Response.Headers.Add(
                        "content-disposition", "attachment; filename=export.xlsx");
                }
#pragma warning disable 1998 
                return new FileCallbackResult(new MediaTypeHeaderValue(Response.ContentType),
                    async (outputStream, _) =>
                    {
                        workbookResult.Value.Write(outputStream, false);
                    });
#pragma warning restore 1998
            });
        }

    class ReadeResult {
        public DataStructureQuery DataStructureQuery { get; set; }
        public IEnumerable<IEnumerable<object>> Rows { get; set; }
    }

    private Result<ReadeResult, IActionResult> ReadRows(EntityExportInfo entityExportInfo, DataStructureQuery dataStructureQuery) {
            var result = ExecuteDataReader(
                               dataStructureQuery: dataStructureQuery,
                               methodId: entityExportInfo.LazyLoadedEntityInput.MenuId);
            var readerResult = new ReadeResult 
            {
                DataStructureQuery = dataStructureQuery,
                Rows = result.Value as IEnumerable<IEnumerable<object>>
            };
            return result.IsSuccess
                ? Result.Ok<ReadeResult, IActionResult>(readerResult)
                : Result.Failure<ReadeResult, IActionResult>(result.Error);
        }

    private Result<IWorkbook, IActionResult> FillWorkbook(EntityExportInfo entityExportInfo,
        ExcelEntityExporter excelEntityExporter)
    {
            if (entityExportInfo.Store is WorkQueueSessionStore workQueueSessionStore)
            {

               return WorkQueueGetRowsGetRowsQuery(entityExportInfo.LazyLoadedEntityInput, workQueueSessionStore)
                    .Bind(dataStructureQuery => ReadRows(entityExportInfo, dataStructureQuery))
                    .Map(readeResult => excelEntityExporter.FillWorkBook(
                       entityExportInfo, 
                       readeResult.DataStructureQuery
                        .GetAllQueryColumns()
                        .Select(x =>x.Name)
                        .ToList(),
                       readeResult.Rows));               
            }
            bool isLazyLoaded =
                entityExportInfo.Store.IsLazyLoadedEntity(entityExportInfo.Entity);

            if (isLazyLoaded)
            {
                ILazyRowLoadInput input = entityExportInfo.LazyLoadedEntityInput;

                return EntityIdentificationToEntityData(input)
                    .Bind(entityData => GetRowsGetQuery(input, entityData))
                    .Bind(dataStructureQuery => ReadRows(entityExportInfo, dataStructureQuery))
                    .Map(readeResult => excelEntityExporter.FillWorkBook(
                        entityExportInfo,
                        readeResult.DataStructureQuery
                            .GetAllQueryColumns()
                            .Select(x => x.Name)
                            .ToList(),
                        readeResult.Rows));
            }
            IWorkbook workBook = excelEntityExporter.FillWorkBook(entityExportInfo);
            return Result.Ok<IWorkbook, IActionResult>(workBook) ;
        }
}