﻿using System.Globalization;
using Microsoft.AspNetCore.Blazor;

namespace BlazorRedux
{
    public class ReduxDebugger<TModel, TAction> : ReduxComponent<TModel, TAction>
    {
        public RenderFragment Debugger;
        
        protected override void OnInit()
        {
            base.OnInit();

            // ReSharper disable once RedundantAssignment
            Debugger = builder =>
            {
                var seq = 0;

                builder.OpenElement(seq++, "style");
                builder.AddContent(seq++, 
@".redux-debugger__historic-entry {
    background-color: WhiteSmoke;
    margin: .5em;
}

.redux-debugger__historic-entry__action {
    font-weight: bold;
}");
                builder.CloseElement();

                foreach (var entry in Store.History)
                {
                    builder.OpenElement(seq++, "div");
                    builder.AddAttribute(seq++, "class", "redux-debugger__historic-entry");

                    builder.OpenElement(seq++, "div");
                    builder.AddAttribute(seq++, "class", "redux-debugger__historic-entry__action");
                    builder.AddContent(seq++, entry.Action?.ToString() ?? "Initial state");
                    builder.CloseElement();

                    builder.OpenElement(seq++, "div");
                    builder.AddAttribute(seq++, "class", "redux-debugger__historic-entry__time");
                    builder.AddContent(seq++, entry.Time.ToUniversalTime().ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'"));
                    builder.CloseElement();

                    builder.CloseElement();
                }
            };
        }
    }
}
