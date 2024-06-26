﻿using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Origam.Server.Controller;
public class FileCallbackResult : FileResult
{
    private Func<Stream, ActionContext, Task> _callback;
    public FileCallbackResult(MediaTypeHeaderValue contentType, Func<Stream, ActionContext, Task> callback)
        : base(contentType?.ToString())
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }
    public override Task ExecuteResultAsync(ActionContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        var executor = new FileCallbackResultExecutor(context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>());
        return executor.ExecuteAsync(context, this);
    }
    private sealed class FileCallbackResultExecutor : FileResultExecutorBase
    {
        public FileCallbackResultExecutor(ILoggerFactory loggerFactory)
            : base(CreateLogger<FileCallbackResultExecutor>(loggerFactory))
        {
        }
        public Task ExecuteAsync(ActionContext context, FileCallbackResult result)
        {
            // SetHeadersAndLog(context, result);
            return result._callback(context.HttpContext.Response.Body, context);
        }
    }
}
