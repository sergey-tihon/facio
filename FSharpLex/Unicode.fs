﻿(*
Copyright (c) 2012-2013, Jack Pappas
All rights reserved.

This code is provided under the terms of the 2-clause ("Simplified") BSD license.
See LICENSE.TXT for licensing details.
*)

namespace FSharpLex

open FSharpLex.SpecializedCollections

//
[<RequireQualifiedAccess>]
module internal Unicode =
    /// Maps each UnicodeCategory to the set of characters in the category.
    let categoryCharSet =
        // OPTIMIZE : If this takes "too long" to compute on-the-fly, we could pre-compute
        // the category sets and implement code which recreates the CharSets from the intervals
        // in the CharSets (not the individual values, which would be much slower).
        let table = System.Collections.Generic.Dictionary<_,_> (30)
        for i = 0 to 65535 do
            /// The Unicode category of this character.
            let category = System.Char.GetUnicodeCategory (char i)

            // Add this character to the set for this category.
            table.[category] <-
                match table.TryGetValue category with
                | true, charSet ->
                    CharSet.add (char i) charSet
                | false, _ ->
                    CharSet.singleton (char i)

        // TODO : Assert that the table contains an entry for every UnicodeCategory value.
        // Otherwise, exceptions will be thrown at run-time if we try to retrive non-existent entries.

        // Convert the dictionary to a Map
        (Map.empty, table)
        ||> Seq.fold (fun categoryMap kvp ->
            Map.add kvp.Key kvp.Value categoryMap)

