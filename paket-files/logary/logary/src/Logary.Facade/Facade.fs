/// The logging namespace, which contains the logging abstraction for this
/// library. See https://github.com/logary/logary for details. This module is
/// completely stand-alone in that it has no external references and its adapter
/// in Logary has been well tested.
namespace Suave.Logging

open System
open System.Runtime.CompilerServices

/// The log level denotes how 'important' the gauge or event message is.
[<CustomEquality; CustomComparison>]
type LogLevel =
  /// The log message is not that important; can be used for intricate debugging.
  | Verbose
  /// The log message is at a default level, debug level. Useful for shipping to
  /// infrastructure that further processes it, but not so useful for human
  /// inspection in its raw format, except during development.
  | Debug
  /// The log message is informational; e.g. the service started, stopped or
  /// some important business event occurred.
  | Info
  /// The log message is a warning; e.g. there was an unhandled exception or
  /// an even occurred which was unexpected. Sometimes human corrective action
  /// is needed.
  | Warn
  /// The log message is at an error level, meaning an unhandled exception
  /// occurred at a location where it is deemed important to keeping the service
  /// running. A human should take corrective action.
  | Error
  /// The log message denotes a fatal error which cannot be recovered from. The
  /// service should be shut down. Human corrective action is needed.
  | Fatal

  /// Converts the LogLevel to a string
  override x.ToString () =
    match x with
    | Verbose -> "verbose"
    | Debug   -> "debug"
    | Info    -> "info"
    | Warn    -> "warn"
    | Error   -> "error"
    | Fatal   -> "fatal"

  /// Converts the string passed to a Loglevel.
  static member ofString (str : string) =
    if str = null then invalidArg "str" "may not be null"
    match str.ToLowerInvariant() with
    | "verbose" -> Verbose
    | "debug"   -> Debug
    | "info"    -> Info
    | "warn"    -> Warn
    | "error"   -> Error
    | "fatal"   -> Fatal
    | _         -> Info

  /// Turn the LogLevel into an integer
  member x.toInt () =
    (function
    | Verbose -> 1
    | Debug   -> 2
    | Info    -> 3
    | Warn    -> 4
    | Error   -> 5
    | Fatal   -> 6) x

  /// Turn an integer into a LogLevel
  static member ofInt i =
    (function
    | 1 -> Verbose
    | 2 -> Debug
    | 3 -> Info
    | 4 -> Warn
    | 5 -> Error
    | 6 -> Fatal
    | _ as i -> failwithf "LogLevel matching integer %i is not available" i) i

  interface IComparable<LogLevel> with
    member x.CompareTo other =
      compare (x.toInt()) (other.toInt())

  static member op_LessThan (a, b) =
    (a :> IComparable<LogLevel>).CompareTo(b) < 0

  static member op_LessThanOrEqual (a, b) =
    (a :> IComparable<LogLevel>).CompareTo(b) <= 0

  static member op_GreaterThan (a, b) =
    (a :> IComparable<LogLevel>).CompareTo(b) > 0

  static member op_GreaterThanOrEqual (a, b) =
    (a :> IComparable<LogLevel>).CompareTo(b) >= 0

  override x.GetHashCode () =
    x.toInt ()

  interface IComparable with
    member x.CompareTo other =
      match other with
      | null ->
        1

      | :? LogLevel as tother ->
        (x :> IComparable<LogLevel>).CompareTo tother

      | _ ->
        failwithf "invalid comparison %A to %A" x other

  interface IEquatable<LogLevel> with
    member x.Equals other =
      x.toInt() = other.toInt()

  override x.Equals other =
    (x :> IComparable).CompareTo other = 0

/// Represents a logged value; either a Gauge or an Event.
type PointValue =
  /// An event is what it sounds like; something occurred and needs to be
  /// logged. Its field is named 'template' because it should not be interpolated
  /// with values; instead these values should be put in the 'fields' field of
  /// the Message.
  | Event of template:string
  /// This is as value for a metric, with a unit attached. The unit can be
  /// something like Seconds or Hz.
  | Gauge of value:int64 * units:string

/// The # of nanoseconds after 1970-01-01 00:00:00.
type EpochNanoSeconds = int64

/// Extensions to DateTime.
module DateTime =

  /// Get the Logary timestamp off the DateTime.
  let timestamp (dt : DateTime) : EpochNanoSeconds =
    (dt.Ticks - DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks)
    * 100L

  /// Get the DateTimeOffset ticks off from the EpochNanoSeconds
  let ticksUTC (epoch : EpochNanoSeconds) : int64 =
    epoch / 100L
    + DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks

/// Extensions to DateTimeOffset.
module DateTimeOffset =

  /// Get the Logary timestamp off the DateTimeOffset.
  let timestamp (dt : DateTimeOffset) : EpochNanoSeconds =
    (dt.Ticks - DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks)
    * 100L

  /// Get the DateTimeOffset ticks from EpochNanoSeconds
  let ticksUTC (epoch : EpochNanoSeconds) : int64 =
    epoch / 100L
    + DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks

/// This is record that is logged. It's capable of representing both metrics
/// (gauges) and events. See https://github.com/logary/logary for details.
type Message =
  { /// The 'path' or 'name' of this data point. Do not confuse template in
    /// (Event template) = message.value
    name      : string[]
    /// The main value for this metric or event. Either a Gauge or an Event. (A
    /// discriminated union type)
    value     : PointValue
    /// The semantic-logging data.
    fields    : Map<string, obj>
    /// When? nanoseconds since UNIX epoch.
    timestamp : EpochNanoSeconds
    /// How important? See the docs on the LogLevel type for details.
    level     : LogLevel }

  /// Gets the ticks for UTC since 0001-01-01 00:00:00 for this message. You
  /// can pass this value into a DateTimeOffset c'tor
  member x.utcTicks =
    DateTimeOffset.ticksUTC x.timestamp

  /// If you're looking for how to transform the Message's fields, then use the
  /// module methods rather than instance methods, since you'll be creating new
  /// values rather than changing an existing value.
  member x.README =
    ()

/// The logger is the interface for calling code to use for logging.
type Logger =
  /// Evaluates the callback if the log level is enabled. Returns an async that
  /// itself completes when the logging infrastructure has finished writing that
  /// Message. Completes directly if nothing is logged. What the ack means from
  /// a durability standpoint depends on the logging infrastructure you're using
  /// behind this facade. Will not block, besides doing the computation inside
  /// the callback. You should not do blocking operations in the callback.
  abstract member logWithAck : LogLevel -> (LogLevel -> Message) -> Async<unit>

  /// Evaluates the callback if the log level is enabled. Will not block,
  /// besides doing the computation inside the callback. You should not do
  /// blocking operations in the callback.
  abstract member log : LogLevel -> (LogLevel -> Message) -> unit

  /// Logs the message without awaiting the logging infrastructure's ack of
  /// having successfully written the log message. What the ack means from a
  /// durability standpoint depends on the logging infrastructure you're using
  /// behind this facade.
  abstract member logSimple : Message -> unit

/// Syntactic sugar on top of Logger for F# libraries.
[<AutoOpen>]
module internal LoggerEx =
  type Logger with
    member x.verbose (msgFactory : LogLevel -> Message) : unit =
      x.log Verbose msgFactory

    member x.debug (msgFactory : LogLevel -> Message) : unit =
      x.log Debug msgFactory

    member x.info msgFactory : unit =
      x.log Info msgFactory

    member x.warn msgFactory : unit =
      x.log Warn msgFactory

    member x.error msgFactory : unit =
      x.log Error msgFactory

    member x.fatal msgFactory : unit =
      x.log Fatal msgFactory

type LoggingConfig =
  { timestamp : unit -> int64
    getLogger : string[] -> Logger }

/// The theme-able tokens of the messages generated by the LiterateConsoleTarget.
type LiterateToken =
  | Text | Subtext
  | Punctuation
  | LevelVerbose | LevelDebug | LevelInfo | LevelWarning | LevelError | LevelFatal
  | KeywordSymbol | NumericSymbol | StringSymbol | OtherSymbol | NameSymbol
  | MissingTemplateField

/// The options used by the LiterateConsoleTarget.
type LiterateOptions =
  { formatProvider: IFormatProvider
    theme: LiterateToken -> ConsoleColor
    getLogLevelText: LogLevel -> string
    printTemplateFieldNames: bool }
  static member create(?formatProvider) =
    // note: literate is meant for human consumption, and so the default
    // format provider of 'Current' is appropriate here. The reader expects
    // to see the dates, numbers, currency, etc formatted in the local culture
    { formatProvider = defaultArg formatProvider Globalization.CultureInfo.CurrentCulture
      getLogLevelText = function
              | Debug ->    "DBG"
              | Error ->    "ERR"
              | Fatal ->    "FTL"
              | Info ->     "INF"
              | Verbose ->  "VRB"
              | Warn ->     "WRN"
      theme = function
              | Text -> ConsoleColor.White
              | Subtext -> ConsoleColor.Gray
              | Punctuation -> ConsoleColor.DarkGray
              | LevelVerbose -> ConsoleColor.Gray
              | LevelDebug -> ConsoleColor.Gray
              | LevelInfo -> ConsoleColor.White
              | LevelWarning -> ConsoleColor.Yellow
              | LevelError -> ConsoleColor.Red
              | LevelFatal -> ConsoleColor.Red
              | KeywordSymbol -> ConsoleColor.Blue
              | NumericSymbol -> ConsoleColor.Magenta
              | StringSymbol -> ConsoleColor.Cyan
              | OtherSymbol -> ConsoleColor.Green
              | NameSymbol -> ConsoleColor.Gray
              | MissingTemplateField -> ConsoleColor.Red

      printTemplateFieldNames = false }
  static member createInvariant() =
    LiterateOptions.create(Globalization.CultureInfo.InvariantCulture)

[<AutoOpen>]
module internal Literals =
  [<Literal>]
  let internal FieldExnKey = "exn"

  [<Literal>]
  let FieldErrorsKey = "errors"

module internal FsMtParser =
  open System.Text

  type Property(name : string, format : string) =
    static let emptyInstance = Property("", null)
    static member empty = emptyInstance
    member x.name = name
    member x.format = format
    member internal x.AppendPropertyString(sb : StringBuilder, ?replacementName) =
      sb.Append("{")
        .Append(defaultArg replacementName name)
        .Append(match x.format with null | "" -> "" | _ -> ":" + x.format)
        .Append("}")
    override x.ToString() = x.AppendPropertyString(StringBuilder()).ToString()

  module internal ParserBits =

    let inline isLetterOrDigit c = System.Char.IsLetterOrDigit c
    let inline isValidInPropName c = c = '_' || System.Char.IsLetterOrDigit c
    let inline isValidInFormat c = c <> '}' && (c = ' ' || isLetterOrDigit c || System.Char.IsPunctuation c)
    let inline isValidCharInPropTag c = c = ':' || isValidInPropName c || isValidInFormat c

    [<Struct>]
    type Range(startIndex : int, endIndex : int) =
      member inline x.start = startIndex
      member inline x.``end`` = endIndex
      member inline x.length = (endIndex - startIndex) + 1
      member inline x.getSubstring (s : string) = s.Substring(startIndex, x.length)
      member inline x.isEmpty = startIndex = -1 && endIndex = -1
      static member inline substring (s : string, startIndex, endIndex) = s.Substring(startIndex, (endIndex - startIndex) + 1)
      static member inline empty = Range(-1, -1)

    let inline tryGetFirstCharInRange predicate (s : string) (range : Range) =
      let rec go i =
        if i > range.``end`` then -1
        else if not (predicate s.[i]) then go (i+1) else i
      go range.start

    let inline tryGetFirstChar predicate (s : string) first =
      tryGetFirstCharInRange predicate s (Range(first, s.Length - 1))

    let inline hasAnyInRange predicate (s : string) (range : Range) =
      match tryGetFirstChar (predicate) s range.start with
      | -1 ->
        false
      | i ->
        i <= range.``end``

    let inline hasAny predicate (s : string) = hasAnyInRange predicate s (Range(0, s.Length - 1))
    let inline indexOfInRange s range c = tryGetFirstCharInRange ((=) c) s range

    let inline tryGetPropInRange (template : string) (within : Range) : Property =
      // Attempts to validate and parse a property token within the specified range inside
      // the template string. If the property insides contains any invalid characters,
      // then the `Property.Empty' instance is returned (hence the name 'try')
      let nameRange, formatRange =
        match indexOfInRange template within ':' with
        | -1 ->
          within, Range.empty // no format
        | formatIndex ->
          Range(within.start, formatIndex-1), Range(formatIndex+1, within.``end``) // has format part
      let propertyName = nameRange.getSubstring template
      if propertyName = "" || (hasAny (not<<isValidInPropName) propertyName) then
        Property.empty
      elif (not formatRange.isEmpty) && (hasAnyInRange (not<<isValidInFormat) template formatRange) then
        Property.empty
      else
        let format = if formatRange.isEmpty then null else formatRange.getSubstring template
        Property(propertyName, format)

    let findNextNonPropText (startAt : int) (template : string) (foundText : string->unit) : int =
      // Finds the next text token (starting from the 'startAt' index) and returns the next character
      // index within the template string. If the end of the template string is reached, or the start
      // of a property token is found (i.e. a single { character), then the 'consumed' text is passed
      // to the 'foundText' method, and index of the next character is returned.
      let rec go i =
        if i >= template.Length then template.Length
        else
          match template.[i] with
          | '{' ->
            if (i+1) < template.Length && template.[i+1] = '{' then go (i+2) else i
          | '}' when (i+1) < template.Length && template.[i+1] = '}' ->
            go (i+2)
          | _ ->
            go (i+1)
      let nextIndex = go startAt
      if (nextIndex > startAt) then
        foundText (Range.substring(template, startAt, nextIndex - 1))
      nextIndex

    let findPropOrText (start : int) (template : string)
                       (foundText : string -> unit)
                       (foundProp : Property -> unit) : int =
      // Attempts to find the indices of the next property in the template
      // string (starting from the 'start' index). Once the start and end of
      // the property token is known, it will be further validated (by the
      // tryGetPropInRange method). If the range turns out to be invalid, it's
      // not a property token, and we return it as text instead. We also need
      // to handle some special case here: if the end of the string is reached,
      // without finding the close brace (we just signal 'foundText' in that case).
      let nextInvalidCharIndex =
        match tryGetFirstChar (not << isValidCharInPropTag) template (start+1) with
        | -1 ->
          template.Length
        | idx ->
          idx

      if nextInvalidCharIndex = template.Length || template.[nextInvalidCharIndex] <> '}' then
        foundText (Range.substring(template, start, (nextInvalidCharIndex - 1)))
        nextInvalidCharIndex
      else
        let nextIndex = nextInvalidCharIndex + 1
        let propInsidesRng = Range(start + 1, nextIndex - 2)
        match tryGetPropInRange template propInsidesRng with
        | prop when not (obj.ReferenceEquals(prop, Property.empty)) ->
          foundProp prop
        | _ ->
          foundText (Range.substring(template, start, (nextIndex - 1)))
        nextIndex

  /// Parses template strings such as "Hello, {PropertyWithFormat:##.##}"
  /// and calls the 'foundTextF' or 'foundPropF' functions as the text or
  /// property tokens are encountered.
  let parseParts (template : string) foundTextF foundPropF =
    let tlen = template.Length
    let rec go start =
      if start >= tlen then ()
      else match ParserBits.findNextNonPropText start template foundTextF with
            | next when next <> start ->
              go next
            | _ ->
              go (ParserBits.findPropOrText start template foundTextF foundPropF)
    go 0

module internal Formatting =
  open System.Text

  let literateFormatValue (options : LiterateOptions) (fields : Map<string, obj>) = function
    | Event template ->
      let themedParts = ResizeArray<string * LiterateToken>()
      let matchedFields = ResizeArray<string>()
      let foundText (text: string) = themedParts.Add (text, Text)
      let foundProp (prop: FsMtParser.Property) =
        match Map.tryFind prop.name fields with
        | Some propValue ->
          // render using string.Format, so the formatting is applied
          let stringFormatTemplate = prop.AppendPropertyString(StringBuilder(), "0").ToString()
          let fieldAsText = String.Format (options.formatProvider, stringFormatTemplate, [| propValue |])
          // find the right theme colour based on data type
          let valueColour =
            match propValue with
            | :? bool ->
              KeywordSymbol
            | :? int16 | :? int32 | :? int64 | :? decimal | :? float | :? double ->
              NumericSymbol
            | :? string | :? char ->
              StringSymbol
            | _ ->
              OtherSymbol
          if options.printTemplateFieldNames then
            themedParts.Add ("["+prop.name+"] ", Subtext)
          matchedFields.Add prop.name
          themedParts.Add (fieldAsText, valueColour)

        | None ->
          themedParts.Add (prop.ToString(), MissingTemplateField)

      FsMtParser.parseParts template foundText foundProp
      Set.ofSeq matchedFields, List.ofSeq themedParts

    | Gauge (value, units) ->
      Set.empty, [ sprintf "%i" value, NumericSymbol
                   sprintf "%s" units, KeywordSymbol ]

  let formatValue (fields : Map<string, obj>) (pv : PointValue) =
    let matchedFields, themedParts =
      literateFormatValue (LiterateOptions.createInvariant()) fields pv
    matchedFields, System.String.Concat(themedParts |> List.map fst)

  let literateExceptionColorizer (options : LiterateOptions) (ex : exn) =
    let stackFrameLinePrefix = "   "
    use exnLines = new System.IO.StringReader(ex.ToString())
    let rec go lines =
      match exnLines.ReadLine() with
      | null ->
        List.rev lines // finished reading
      | line ->
        if line.StartsWith(stackFrameLinePrefix) then
          // subtext
          go ((Environment.NewLine, Text) :: ((line, Subtext) :: lines))
        else
          // regular text
          go ((Environment.NewLine, Text) :: ((line, Text) :: lines))
    go []

  let literateColorizeExceptions (context : LiterateOptions) message =
    let exnExceptionParts =
      match message.fields.TryFind FieldExnKey with
      | Some (:? Exception as ex) ->
        literateExceptionColorizer context ex
        @ [ Environment.NewLine, Text ]
      | _ ->
        [] // there is no spoon
    let errorsExceptionParts =
      match message.fields.TryFind FieldErrorsKey with
      | Some (:? List<obj> as exnListAsObjList) ->
        exnListAsObjList |> List.collect (function
          | :? exn as ex ->
            literateExceptionColorizer context ex
            @ [ Environment.NewLine, Text ]
          | _ ->
            [])
      | _ ->
        []

    exnExceptionParts @ errorsExceptionParts

  /// Split a structured message up into theme-able parts (tokens), allowing the
  /// final output to display to a user with colours to enhance readability.
  let literateDefaultTokenizer (options : LiterateOptions) (message : Message) : (string * LiterateToken) list =
    let formatLocalTime (utcTicks : int64) =
      DateTimeOffset(utcTicks, TimeSpan.Zero).LocalDateTime.ToString("HH:mm:ss", options.formatProvider),
      Subtext

    let themedMessageParts =
      message.value |> literateFormatValue options message.fields |> snd

    let themedExceptionParts =
      let exnParts = literateColorizeExceptions options message
      if not exnParts.IsEmpty then
        [ Environment.NewLine, Text ]
        @ exnParts
        @ [ Environment.NewLine, Text ]
      else []

    let getLogLevelToken = function
      | Verbose -> LevelVerbose
      | Debug -> LevelDebug
      | Info -> LevelInfo
      | Warn -> LevelWarning
      | Error -> LevelError
      | Fatal -> LevelFatal

    [ "[", Punctuation
      formatLocalTime message.utcTicks
      " ", Subtext
      options.getLogLevelText message.level, getLogLevelToken message.level
      "] ", Punctuation ]
    @ themedMessageParts
    @ themedExceptionParts

  let literateDefaultColorWriter sem (parts : (string * ConsoleColor) list) =
    lock sem <| fun _ ->
      parts |> List.iter (fun (text, color) ->
        Console.ForegroundColor <- color
        Console.Write(text)
      )
      Console.ResetColor()

  /// let the ISO8601 love flow
  let defaultFormatter (message : Message) =
    let app (x : obj) (sb : StringBuilder) =
      sb.Append x |> ignore

    let formatLevel (level : LogLevel) =
      "[" + Char.ToUpperInvariant(level.ToString().[0]).ToString() + "] "

    let formatInstant (utcTicks : int64) =
      (DateTimeOffset(utcTicks, TimeSpan.Zero).ToString("o")) + ": "

    let formatName (name : string[]) =
      " [" + String.concat "." name + "]"

    let formatExn (fields : Map<string, obj>) =
      match fields |> Map.tryFind FieldExnKey with
      | None ->
        String.Empty

      | Some ex ->
        " exn:\n" + ex.ToString()

    let formatFields (ignored : Set<string>) (fields : Map<string, obj>) =
      if not (Map.isEmpty fields) then
        fields
        |> Seq.filter (fun (KeyValue (k, _)) ->
          not (ignored |> Set.contains k))
        |> Seq.map (fun (KeyValue (k, v)) ->
          sprintf "\n - %s: %O" k v)
        |> String.concat ""
      else
        ""

    let matchedFields, valueString =
      formatValue message.fields message.value

    // [I] 2014-04-05T12:34:56Z: Hello World! [my.sample.app]
    formatLevel message.level +
    formatInstant message.utcTicks +
    valueString +
    formatName message.name +
    formatExn message.fields +
    formatFields matchedFields message.fields

/// Logs a line in a format that is great for human consumption,
/// using console colours to enhance readability.
/// Sample: [10:30:49 INF] User "AdamC" began the "checkout" process with 100 cart items
type LiterateConsoleTarget(minLevel, ?options, ?literateTokenizer, ?outputWriter, ?consoleSemaphore) =
  let sem           = defaultArg consoleSemaphore (obj())
  let options       = defaultArg options (LiterateOptions.create())
  let tokenize      = defaultArg literateTokenizer Formatting.literateDefaultTokenizer
  let colorWriter   = defaultArg outputWriter Formatting.literateDefaultColorWriter sem

  let colorizeThenNewLine message =
    (tokenize options message) @ [Environment.NewLine, Text]
    |> List.map (fun (s, t) ->
      s, options.theme(t))

  interface Logger with
    member x.logWithAck level msgFactory =
      if level >= minLevel then
        colorWriter (colorizeThenNewLine (msgFactory level))
      async.Return ()
    member x.log level msgFactory =
      if level >= minLevel then
        colorWriter (colorizeThenNewLine (msgFactory level))
    member x.logSimple msg =
      if msg.level >= minLevel then
        colorWriter (colorizeThenNewLine msg)

/// Log a line with the given format, printing the current time in UTC ISO-8601 format
/// and then the string, like such:
/// '2013-10-13T13:03:50.2950037Z: today is the day'
type ConsoleWindowTarget(minLevel, ?formatter, ?colourise, ?originalColor, ?consoleSemaphore) =
  let sem           = defaultArg consoleSemaphore (obj())
  let originalColor = defaultArg originalColor Console.ForegroundColor
  let formatter     = defaultArg formatter Formatting.defaultFormatter
  let colourise     = defaultArg colourise true
  let write         = System.Console.WriteLine : string -> unit

  let toColour = function
    | LogLevel.Verbose -> ConsoleColor.DarkGreen
    | LogLevel.Debug   -> ConsoleColor.Green
    | LogLevel.Info    -> ConsoleColor.White
    | LogLevel.Warn    -> ConsoleColor.Yellow
    | LogLevel.Error   -> ConsoleColor.DarkRed
    | LogLevel.Fatal   -> ConsoleColor.Red

  let log color message =
    if colourise then
      lock sem <| fun _ ->
        Console.ForegroundColor <- color
        message |> formatter |> write
        Console.ForegroundColor <- originalColor
    else
      // we don't need to take another lock, since Console.WriteLine does that for us
      (write << formatter) message

  interface Logger with
    member x.logWithAck level msgFactory =
      if level >= minLevel then
        log (toColour level) (msgFactory level)
      async.Return ()

    member x.log level msgFactory =
      if level >= minLevel then
        log (toColour level) (msgFactory level)

    member x.logSimple msg =
      if msg.level >= minLevel then
        log (toColour msg.level) msg

type OutputWindowTarget(minLevel, ?formatter) =
  let formatter = defaultArg formatter Formatting.defaultFormatter
  let log msg = System.Diagnostics.Debug.WriteLine(formatter msg)

  interface Logger with
    member x.log level msgFactory =
      if level >= minLevel then log (msgFactory level)

    member x.logWithAck level msgFactory =
      if level >= minLevel then log (msgFactory level)
      async.Return ()

    member x.logSimple msg =
      if msg.level >= minLevel then log msg

/// A logger to use for combining a number of other loggers
type CombiningTarget(otherLoggers : Logger list) =
  let sendToAll level msgFactory =
    async {
      let! _ =
        otherLoggers
        |> List.map (fun l ->
          l.logWithAck level msgFactory)
        |> Async.Parallel
      return ()
    }

  interface Logger with
    member x.logWithAck level msgFactory =
      sendToAll level msgFactory

    member x.log level msgFactory =
      for logger in otherLoggers do
        logger.log level msgFactory

    member x.logSimple msg =
      sendToAll msg.level (fun _ -> msg)
      |> Async.Start

module Targets =

  let create level =
    if level >= LogLevel.Info then
      ConsoleWindowTarget(level) :> Logger
    else
      CombiningTarget(
        [ ConsoleWindowTarget(level)
          OutputWindowTarget(level) ])
      :> Logger

module Global =

  /// The global default configuration, which logs to Console at Info level.
  let DefaultConfig =
    { timestamp = fun () -> DateTimeOffset.timestamp DateTimeOffset.UtcNow
      getLogger = fun _ -> ConsoleWindowTarget(Info) :> Logger }

  let private config = ref DefaultConfig
  let private locker = obj ()

  /// The flyweight just references the current configuration. If you want
  /// multiple per-process logging setups, then don't use the static methods,
  /// but instead pass a Logger instance around, setting the name field of the
  /// Message value you pass into the logger.
  type internal Flyweight(name : string[]) =
    let initialLogger = (!config).getLogger name
    let mutable actualLogger : Logger option = None

    let withLogger action =
      let logger =
        if Object.ReferenceEquals(!config, DefaultConfig) then
          initialLogger
        elif actualLogger = None then
          lock locker <| fun _ ->
            if actualLogger = None then
              let logger' = (!config).getLogger name
              actualLogger <- Some logger'
              logger'
            else
              actualLogger |> Option.get
        else
          actualLogger |> Option.get

      action logger

    let ensureName (m : Message) =
      if Array.isEmpty m.name then { m with name = name } else m

    interface Logger with
      member x.log level msgFactory =
        withLogger (fun logger -> logger.log level (msgFactory >> ensureName))

      member x.logWithAck level msgFactory =
        withLogger (fun logger -> logger.logWithAck level (msgFactory >> ensureName))

      member x.logSimple message =
        withLogger (fun logger -> logger.logSimple (ensureName message))

  let internal getStaticLogger (name : string []) =
    Flyweight name

  let timestamp () : EpochNanoSeconds =
    (!config).timestamp ()

  /// Call from the initialisation of your library. Initialises the
  /// Logary.Facade globally/per process.
  let initialise cfg =
    config := cfg

/// Module for acquiring static loggers (when you don't want or can't)
/// pass loggers as values.
module Log =

  /// Create a named logger. Full stop (.) acts as segment delimiter in the
  /// hierachy of namespaces and loggers.
  let create (name : string) =
    if name = null then invalidArg "name" "name is null"
    Global.getStaticLogger (name.Split([|'.'|], StringSplitOptions.RemoveEmptyEntries))
    :> Logger

  /// Create an hierarchically named logger
  let createHiera (name : string[]) =
    if name = null then invalidArg "name" "name is null"
    if name.Length = 0 then invalidArg "name" "must have >0 segments"
    Global.getStaticLogger name
    :> Logger

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Message =

  /// Create a new event log message.
  let event level template =
    { name      = [||]
      value     = Event template
      fields    = Map.empty
      timestamp = Global.timestamp ()
      level     = level }

  /// Create a new event log message – like `event` but with parameters flipped.
  /// Useful to use with `Logger.log` with point-free style, to reduce the
  /// noise.
  let eventX template level =
    event level template

  /// Create a new instantaneous value in a log message.
  let gauge value units =
    { name      = [||]
      value     = Gauge (value, units)
      fields    = Map.empty
      timestamp = Global.timestamp ()
      level     = Debug }

  /// Sets the name/path of the log message.
  let setName (name : string[]) (x : Message) =
    { x with name = name }

  /// Sets the name as a single string; if this string contains dots, the string
  /// will be split on these dots.
  let setSingleName (name : string) (x : Message) =
    if name = null then invalidArg "name" "may not be null"

    let name' =
      name.Split([|'.'|], StringSplitOptions.RemoveEmptyEntries)

    x |> setName name'

  /// Sets the value of the field on the log message.
  let setField (key : string) (value : obj) (x : Message) =
    { x with fields = x.fields |> Map.add key value }

  /// Alias to `setField`
  let setFieldValue = setField

  /// Sets the timestamp on the log message.
  let setTimestamp (ts : EpochNanoSeconds) (x : Message) =
    { x with timestamp = ts }

  /// Sets the level on the log message.
  let setLevel (level : LogLevel) (x : Message) =
    { x with level = level }

  /// Adds an exception to the Message, to the 'errors' field, inside a list.
  let addExn ex (x : Message) =
    let fields' =
      match Map.tryFind FieldErrorsKey x.fields with
      | None ->
        x.fields |> Map.add FieldErrorsKey (box [ box ex ])

      | Some errors ->
        let arr : obj list = unbox errors
        x.fields |> Map.add FieldErrorsKey (box (box ex :: arr))

    { x with fields = fields' }
