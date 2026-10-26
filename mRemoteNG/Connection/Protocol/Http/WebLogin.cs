using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace mRemoteNG.Connection.Protocol.Http
{
    // sysprep fork: logs in to the login form of a web interface (OPNsense, Citrix Web Studio, StoreFront, ...) with
    // the connection's username and password. The fields are found by script; the text and the Enter key go in
    // through the DevTools protocol as real keyboard input, so every web framework sees what a user would type.
    // Automatic logins are limited: only while the tab is new, on pages with few input fields, and never twice on
    // the same login page - a wrong password must not be sent over and over (account lockout).
    [SupportedOSPlatform("windows")]
    internal sealed class WebLogin
    {
        private const int MaxAutomaticSubmits = 3;
        private const int MaxInputsOnLoginPage = 5;

        // Finds the login fields: the first visible password field and the text field before it, or - on the first
        // page of a two-step login - a single field that looks like a username. 'find' returns what is there,
        // 'user'/'pass' focus that field and select its content.
        private const string Script = @"(function(mode){
  function visible(e){ const r=e.getBoundingClientRect(); const s=getComputedStyle(e);
    return r.width>0 && r.height>0 && s.visibility!=='hidden' && s.display!=='none' && !e.disabled && !e.readOnly; }
  const inputs=[...document.querySelectorAll('input')].filter(i=>visible(i) && !['hidden','checkbox','radio','submit','button','image','file'].includes(i.type));
  const textTypes=['text','email','tel',''];
  const pass=inputs.find(i=>i.type==='password')||null;
  let user=null;
  if(pass){ for(let i=inputs.indexOf(pass)-1;i>=0;i--){ if(textTypes.includes(inputs[i].type)){ user=inputs[i]; break; } } }
  else { user=inputs.filter(i=>textTypes.includes(i.type)).find(i=>/user|login|email|account|name/i.test([i.name,i.id,i.autocomplete,i.placeholder,i.getAttribute('aria-label')].join(' ')))||null; }
  if(mode==='find') return {user:!!user, pass:!!pass, inputs:inputs.length, key:location.href+'|'+(!!user)+'|'+(!!pass)};
  const target=mode==='user'?user:pass;
  if(!target) return false;
  target.focus(); if(target.select) target.select(); return true;
})('__MODE__')";

        private readonly HashSet<string> _submittedPages = [];
        private int _automaticSubmits;
        private bool _busy;

        public bool AutomaticLoginDone { get; private set; }

        // Fills in the login fields on the current page and presses Enter. Returns true if it found fields.
        public async Task<bool> LoginAsync(CoreWebView2 webView, string username, string password, bool automatic)
        {
            if (_busy || string.IsNullOrEmpty(username)) return false;
            _busy = true;
            try
            {
                using JsonDocument found = JsonDocument.Parse(await webView.ExecuteScriptAsync(Script.Replace("__MODE__", "find")));
                if (found.RootElement.ValueKind != JsonValueKind.Object) return false;
                bool hasUser = found.RootElement.GetProperty("user").GetBoolean();
                bool hasPassword = found.RootElement.GetProperty("pass").GetBoolean();
                if (!hasUser && !hasPassword)
                {
                    // nothing (left) to log in to - after an automatic login: logged in, stop trying
                    if (automatic && _automaticSubmits > 0) AutomaticLoginDone = true;
                    return false;
                }

                if (automatic)
                {
                    if (_automaticSubmits >= MaxAutomaticSubmits || found.RootElement.GetProperty("inputs").GetInt32() > MaxInputsOnLoginPage)
                        return false;
                    // a login page that was sent already: still loading, or the login failed - never send it twice
                    if (!_submittedPages.Add(found.RootElement.GetProperty("key").GetString() ?? ""))
                        return false;
                }

                if (hasUser && await FocusAsync(webView, "user"))
                    await TypeAsync(webView, username);
                if (hasPassword && !string.IsNullOrEmpty(password) && await FocusAsync(webView, "pass"))
                    await TypeAsync(webView, password);
                await PressEnterAsync(webView);

                if (automatic) _automaticSubmits++;
                return true;
            }
            catch (Exception)
            {
                // a page that is just navigating away, or no script access - the next attempt or the button will do
                return false;
            }
            finally
            {
                _busy = false;
            }
        }

        private static async Task<bool> FocusAsync(CoreWebView2 webView, string field)
        {
            return await webView.ExecuteScriptAsync(Script.Replace("__MODE__", field)) == "true";
        }

        private static Task TypeAsync(CoreWebView2 webView, string text)
        {
            return webView.CallDevToolsProtocolMethodAsync("Input.insertText", JsonSerializer.Serialize(new { text }));
        }

        private static async Task PressEnterAsync(CoreWebView2 webView)
        {
            await webView.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                JsonSerializer.Serialize(new { type = "keyDown", key = "Enter", code = "Enter", windowsVirtualKeyCode = 13, nativeVirtualKeyCode = 13, text = "\r", unmodifiedText = "\r" }));
            await webView.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                JsonSerializer.Serialize(new { type = "keyUp", key = "Enter", code = "Enter", windowsVirtualKeyCode = 13, nativeVirtualKeyCode = 13 }));
        }
    }
}
