module Suave.Tests

module Resp =
  open System.Net.Http
  let content (r : HttpResponseMessage) = r.Content
  
open System
open System.Threading
open System.Net.Http

open Suave.Types
open Suave.Web
open Suave.Http

open Fuchu
open Resp

type Method =
  | GET
  | POST
  | DELETE
  | PUT
  | HEAD
  | CONNECT
  | PATCH
  | TRACE
  | OPTIONS

module RequestFactory =
  type SuaveTestCtx =
    { cts          : CancellationTokenSource
    ; suave_config : SuaveConfig }

  let dispose_context (ctx : SuaveTestCtx) =
    ctx.cts.Cancel()
    ctx.cts.Dispose()

  let run_with_factory factory config web_parts : SuaveTestCtx =
    let binding = config.bindings.Head
    let base_uri = binding.ToString()
    let cts = new CancellationTokenSource()
//    cts.Token.Register(fun () -> Log.log "tests:run_with - cancelled") |> ignore
    let config' = { config with ct = cts.Token }

    let listening, server = factory config web_parts
    Async.Start(server, cts.Token)
    // Log.log "tests:run_with_factory -> listening"
    listening |> Async.RunSynchronously // wait for the server to start listening
    // Log.log "tests:run_with_factory <- listening"

    { cts = cts
    ; suave_config = config' }

  let run_with = run_with_factory web_server_async

  let req (methd : Method) (resource : string) ctx =
    let server = ctx.suave_config.bindings.Head.ToString()
    let uri_builder = UriBuilder server
    uri_builder.Path <- resource
    use client = new System.Net.Http.HttpClient()
//    Log.log "tests:req GET %O -> execute" uri_builder.Uri
    let res = client.GetAsync(uri_builder.Uri, HttpCompletionOption.ResponseContentRead, ctx.cts.Token).Result
//    Log.log "tests:req GET %O <- execute" uri_builder.Uri
    dispose_context ctx
    res.Content.ReadAsStringAsync().Result

[<Tests>]
let smoking =
  testList "smoking hot" [
    testCase "smoke" <| fun _ -> Assert.Equal("smoke test", true, true)
  ]

[<Tests>]
let utilities =
  testList "trying some utility functions" [
    testCase "loopback ipv4" <| fun _ ->
      Assert.Equal("127.0.0.1 is a local address", true, is_local_address "127.0.0.1")

    testCase "loopback ipv6" <| fun _ ->
      Assert.Equal("::0 is a local address", true, is_local_address "::1")
  ]

open RequestFactory

[<Tests>]
let gets =
  let run_with' = run_with default_config

  testList "getting basic responses"
    [
      testCase "200 OK returns 'a'" <| fun _ ->
        Assert.Equal("expecting non-empty response", "a", run_with' (OK "a") |> req GET "/")

      testProperty "200 OK returns equivalent" <| fun resp_str ->
        (run_with' (OK resp_str) |> req GET "/hello") = resp_str

      testCase "204 No Content empty body" <| fun _ ->
        Assert.Equal("empty string should always be returned by 204 No Content",
                     "", (run_with' NO_CONTENT |> req GET "/"))
    ]

open OpenSSL.X509
open OpenSSL.Core

[<Tests>]
let proxy =
  let bind :: _ = default_config.bindings
  let to_target r = Some(bind.ip, bind.port)

  let run_target = run_with default_config

  let run_in_context item f_finally f_body =
    try
      f_body item
    finally
      f_finally item

  //  let sslCert = X509Certificate.FromPKCS12(BIO.File("suave.p12","r"), "easy")
  //  let proxy_config = { default_config with bindings = [ HttpBinding.Create(Protocol.HTTPS(sslCert), "127.0.0.1", 8084) ] }
  let proxy_config = { default_config with bindings = [ HttpBinding.Create(Protocol.HTTP, "127.0.0.1", 8084) ] }
  let proxy = run_with_factory Proxy.proxy_server_async proxy_config

  testList "creating proxy" [
    testProperty "GET / returns 200 OK with passed string" <| fun str ->
      run_in_context (run_target (OK str)) dispose_context <| fun _ ->
        Assert.Equal("target's WebPart should return its value", str, proxy to_target |> req GET "/")
    ]

[<EntryPoint>]
let main args =
  let r = defaultMainThisAssembly args
  r
