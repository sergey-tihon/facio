﻿(*

Copyright 2012-2013 Jack Pappas

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

*)

namespace Graham.LR

open LanguagePrimitives
open ExtCore.Control
open ExtCore.Control.Collections
open Graham
open AugmentedPatterns
open Graham.Analysis
open Graham.Graph


/// An LR(1) item.
type Lr1Item<'Nonterminal, 'Terminal
    when 'Nonterminal : comparison
    and 'Terminal : comparison> =
    LrItem<'Nonterminal, 'Terminal, TerminalIndex>

/// An LR(1) parser state -- i.e., a set of LR(1) items.
type Lr1ParserState<'Nonterminal, 'Terminal
    when 'Nonterminal : comparison
    and 'Terminal : comparison> =
    LrParserState<'Nonterminal, 'Terminal, TerminalIndex>

/// LR(1) parser table generation state.
type Lr1TableGenState<'Nonterminal, 'Terminal
    when 'Nonterminal : comparison
    and 'Terminal : comparison> =
    LrTableGenState<'Nonterminal, 'Terminal, TerminalIndex>

/// An LR(1) parser table.
type Lr1ParserTable<'Nonterminal, 'Terminal
    when 'Nonterminal : comparison
    and 'Terminal : comparison> =
    LrParserTable<
        AugmentedNonterminal<'Nonterminal>,
        AugmentedTerminal<'Terminal>,
        TerminalIndex>

/// LR(1) parser tables.
[<RequireQualifiedAccess>]
module Lr1 =
    /// Functions for manipulating LR(1) parser items.
    [<RequireQualifiedAccess>]
    module Item =
        /// Computes the FIRST set of a string of symbols.
        /// The string is a "substring" of a production, followed by a lookahead token.
        let firstSetOfString (taggedProduction : Symbol<NonterminalIndex, TerminalIndex>[]) startIndex lookahead predictiveSets =
            // Preconditions
            if startIndex < 0 then
                invalidArg "startIndex" "The start index cannot be negative."
            elif startIndex > Array.length taggedProduction then
                invalidArg "startIndex" "The start index cannot be greater than the length of the production."

            let productionLength = Array.length taggedProduction

            //
            let rec firstSetOfString firstSet symbolIndex =
                // If we've reached the end of the production,
                // add the lookahead token to the set and return.
                if symbolIndex = productionLength then
                    TagSet.add lookahead firstSet
                else
                    // Match on the current symbol of the production.
                    match taggedProduction.[symbolIndex] with
                    | Symbol.Terminal token ->
                        // Add the token to the set; then, return
                        // because tokens are never nullable.
                        TagSet.add token firstSet

                    | Symbol.Nonterminal nontermId ->
                        /// The FIRST set of this nonterminal symbol.
                        let nontermFirstSet = TagMap.find nontermId predictiveSets.First

                        // Merge the FIRST set of this nonterminal symbol into
                        // the FIRST set of the string.
                        let firstSet = TagSet.union firstSet nontermFirstSet

                        // If this symbol is nullable, continue processing with
                        // the next symbol in the production; otherwise, return.
                        if TagMap.find nontermId predictiveSets.Nullable then
                            firstSetOfString firstSet (symbolIndex + 1)
                        else
                            firstSet

            // Call the recursive implementation to compute the FIRST set.
            firstSetOfString TagSet.empty startIndex

        /// Computes the LR(1) closure of a set of items.
        let rec private closureImpl (taggedGrammar : TaggedGrammar<'Nonterminal, 'Terminal>) predictiveSets items pendingItems
            : LrParserState<_,_,_> =
            match pendingItems with
            | [] ->
                items
            | _ ->
                // Process the worklist.
                let items, pendingItems =
                    ((items, []), pendingItems)
                    ||> List.fold (fun (items, pendingItems) (item : LrItem<_,_,_>) ->
                        // Add the current item to the item set.
                        let items = Set.add item items

                        // If the position is at the end of the production, or if the current symbol
                        // is a terminal, there's nothing that needs to be done for this item.
                        match LrItem.CurrentSymbol item taggedGrammar with
                        | None
                        | Some (Symbol.Terminal _) ->
                            items, pendingItems
                        | Some (Symbol.Nonterminal nonterminal) ->
                            // For all productions of this nonterminal, create a new item
                            // with the parser position at the beginning of the production.
                            // Add these new items into the set of items.
                            let pendingItems =
                                /// The productions of this nonterminal.
                                let nonterminalProductions = TagMap.find nonterminal taggedGrammar.ProductionsByNonterminal

                                /// The FIRST set of the remaining symbols in this production
                                /// (i.e., the symbols following this nonterminal symbol),
                                /// plus the lookahead token from the item.
                                let firstSetOfRemainingSymbols =
                                    let taggedProduction = TagMap.find item.ProductionRuleIndex taggedGrammar.Productions
                                    firstSetOfString taggedProduction (int item.Position + 1) item.Lookahead predictiveSets

                                // For all productions of this nonterminal, create a new item
                                // with the parser position at the beginning of the production.
                                // Add these new items into the set of items.
                                (pendingItems, nonterminalProductions)
                                ||> TagSet.fold (fun pendingItems ruleIndex ->
                                    // Combine the production with each token which could
                                    // possibly follow this nonterminal.
                                    (pendingItems, firstSetOfRemainingSymbols)
                                    ||> TagSet.fold (fun pendingItems nonterminalFollowTokenIndex ->
                                        let newItem = {
                                            ProductionRuleIndex = ruleIndex;
                                            Position = GenericZero;
                                            Lookahead = nonterminalFollowTokenIndex; }

                                        // Only add this item to the worklist if it hasn't been seen yet.
                                        if Set.contains newItem items then pendingItems
                                        else newItem :: pendingItems))

                            // Return the updated item set and worklist.
                            items, pendingItems)

                // Recurse to continue processing.
                // OPTIMIZE : It's not really necessary to reverse the list here -- we could just as easily
                // process the list in reverse but for now we'll process it in order to make the algorithm
                // easier to understand/trace.
                closureImpl taggedGrammar predictiveSets items (List.rev pendingItems)

        /// Computes the LR(1) closure of a set of items.
        let closure (taggedGrammar : TaggedGrammar<'Nonterminal, 'Terminal>) predictiveSets items =
            // Call the recursive implementation, starting with the specified initial item set.
            closureImpl taggedGrammar predictiveSets Set.empty (Set.toList items)

        /// Moves the 'dot' (the current parser position) past the
        /// specified symbol for each item in a set of items.
        let goto symbol items (taggedGrammar : TaggedGrammar<'Nonterminal, 'Terminal>) predictiveSets =
            (Set.empty, items)
            ||> Set.fold (fun updatedItems item ->
                // If the next symbol to be parsed in the production is the
                // specified symbol, create a new item with the position advanced
                // to the right of the symbol and add it to the updated items set.
                match LrItem.CurrentSymbol item taggedGrammar with
                | Some sym when sym = symbol ->
                    let updatedItem =
                        { item with
                            Position = item.Position + 1<_>; }
                    Set.add updatedItem updatedItems

                | _ ->
                    updatedItems)
            // Return the closure of the item set.
            |> closure taggedGrammar predictiveSets


    /// Create an LR(1) parser table for the specified grammar.
    let rec private createTableImpl (taggedGrammar : TaggedAugmentedGrammar<'Nonterminal, 'Terminal>) predictiveSets eofIndex workSet =
        state {
        // If the work-set is empty, we're finished creating the table.
        if TagSet.isEmpty workSet then
            return ()
        else
            let! workSet =
                (TagSet.empty, workSet)
                ||> State.TagSet.fold (fun workSet stateId ->
                    state {
                    /// The current table-generation state.
                    let! tableGenState = State.getState

                    /// The set of parser items for this state.
                    let stateItems = TagBimap.find stateId tableGenState.ParserStates

                    return!
                        (workSet, stateItems)
                        ||> State.Set.fold (fun workSet item ->
                            state {
                            // If the parser position is at the end of the production,
                            // add a 'reduce' action for every terminal (token) in the grammar.
                            match LrItem.CurrentSymbol item taggedGrammar with
                            | None ->
                                // Add a 'reduce' action to the ACTION table entry for this state and lookahead token.
                                do!
                                    let key = stateId, item.Lookahead
                                    LrTableGenState.reduce key item.ProductionRuleIndex

                                // Return the current workset.
                                return workSet
                        
                            | Some symbol ->
                                // Add actions to the table based on the next symbol to be parsed.
                                match symbol with
                                | Symbol.Terminal terminalIndex ->
                                    if terminalIndex = eofIndex then
                                        // When the end-of-file symbol is the next to be parsed,
                                        // add an 'accept' action to the table to indicate the
                                        // input has been parsed successfully.
                                        do! LrTableGenState.accept stateId taggedGrammar

                                        // Return the current workset.
                                        return workSet
                                    else
                                        /// The state (set of items) transitioned into
                                        /// via the edge labeled with this symbol.
                                        let targetState = Item.goto symbol stateItems taggedGrammar predictiveSets

                                        /// The identifier of the target state.
                                        let! isNewState, targetStateId = LrTableGenState.stateId targetState

                                        // If this is a new state, add it to the list of states which need to be visited.
                                        let workSet =
                                            if isNewState then
                                                TagSet.add targetStateId workSet
                                            else workSet

                                        // The next symbol to be parsed is a terminal (token),
                                        // so add a 'shift' action to this entry of the table.
                                        do!
                                            let key = stateId, terminalIndex
                                            LrTableGenState.shift key targetStateId

                                        // Return the current workset.
                                        return workSet

                                | Symbol.Nonterminal nonterminalIndex ->
                                    /// The state (set of items) transitioned into
                                    /// via the edge labeled with this symbol.
                                    let targetState = Item.goto symbol stateItems taggedGrammar predictiveSets

                                    /// The identifier of the target state.
                                    let! isNewState, targetStateId = LrTableGenState.stateId targetState

                                    // If this is a new state, add it to the list of states which need to be visited.
                                    let workSet =
                                        if isNewState then
                                            TagSet.add targetStateId workSet
                                        else workSet

                                    // The next symbol to be parsed is a nonterminal,
                                    // so add a 'goto' action to this entry of the table.
                                    do!
                                        let key = stateId, nonterminalIndex
                                        LrTableGenState.goto key targetStateId

                                    // Return the current workset.
                                    return workSet
                                })})

            // Recurse with the updated table-generation state and work-set.
            return! createTableImpl taggedGrammar predictiveSets eofIndex workSet
        }

    /// Create an LR(1) parser table for the specified grammar.
    let createTable (taggedGrammar : TaggedAugmentedGrammar<'Nonterminal, 'Terminal>)
        : Lr1ParserTable<'Nonterminal, 'Terminal> =
        // Preconditions
        // TODO

        /// Analysis of the augmented grammar.
        let predictiveSets = PredictiveSets.ofGrammar <| TaggedGrammar.toGrammar taggedGrammar

        let workflow =
            state {
            /// The tagged-index of the end-of-file marker.
            let eofIndex = TagBimap.findValue EndOfFile taggedGrammar.Terminals

            /// The identifier for the initial parser state.
            let! (_, initialParserStateId) =
                /// The initial LR state (set of items) passed to 'createTableImpl'.
                let initialParserState : Lr1ParserState<_,_> =
                    let startItems =
                        let startNonterminalIndex = TagBimap.findValue Start taggedGrammar.Nonterminals
                        TagMap.find startNonterminalIndex taggedGrammar.ProductionsByNonterminal

                    (Set.empty, startItems)
                    ||> TagSet.fold (fun items ruleIndex ->
                        // Create an 'item', with the parser position at
                        // the beginning of the production.
                        let item = {
                            ProductionRuleIndex = ruleIndex;
                            Position = GenericZero;
                            // Any token can be used here, because the end-of-file symbol
                            // (in the augmented start production) will never be shifted.
                            // We use the EndOfFile token itself here to keep the code generic.
                            Lookahead = eofIndex; }
                        Set.add item items)
                    |> Item.closure taggedGrammar predictiveSets

                LrTableGenState.stateId initialParserState

            return! createTableImpl taggedGrammar predictiveSets eofIndex (TagSet.singleton initialParserStateId)
            }

        // Execute the workflow to create the parser table.
        LrTableGenState.empty
        |> State.execute workflow

