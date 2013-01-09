﻿(*
Copyright (c) 2012-2013, Jack Pappas
All rights reserved.

This code is provided under the terms of the 2-clause ("Simplified") BSD license.
See LICENSE.TXT for licensing details.
*)

//
namespace FSharpLex.Plugin

open System.ComponentModel.Composition


/// Compiler backends.
[<Export>]
type Backends () =
    let mutable fslexBackend = None

    /// The fslex-compatible backend.
    [<Import>]
    member __.FslexBackend
        with get () : IBackend =
            match fslexBackend with
            | None ->
                invalidOp "The fslex backend has not been set."
            | Some fslexBackend ->
                fslexBackend
        and set value =
            fslexBackend <- Some value
