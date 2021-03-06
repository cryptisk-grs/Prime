﻿// Prime - A PRIMitivEs code library.
// Copyright (C) Bryan Edds, 2013-2020.

namespace Prime
open System
open Prime

/// A publisher-neutral event system.
/// Effectively a mix-in for the 'w type, where 'w is a type that represents the client program.
type EventSystem<'w when 'w :> 'w EventSystem> =
    interface
        inherit PropertySystem<'w>
        abstract member GetLiveness : unit -> Liveness
        abstract member GetGlobalSimulantSpecialized : unit -> Simulant
        abstract member GetGlobalSimulantGeneralized : unit -> GlobalSimulantGeneralized
        abstract member SimulantExists : Simulant -> bool
        abstract member GetEventSystemDelegateHook : unit -> 'w EventSystemDelegate
        abstract member UpdateEventSystemDelegateHook : ('w EventSystemDelegate -> 'w EventSystemDelegate) -> 'w
        abstract member HandleUserDefinedCallback : obj -> obj -> 'w -> Handling * 'w
        abstract member PublishEventHook<'a, 'p when 'p :> Simulant> : Simulant -> 'p -> obj -> 'a Address -> EventTrace -> obj -> 'w -> Handling * 'w
        end

/// Handles simulant property changes.
and SimulantPropertyChangeHandler<'w when 'w :> 'w EventSystem> =
    'w EventSystem -> 'w EventSystem -> 'w EventSystem

/// Detaches a simulant property change handler.
and SimulantPropertyChangeUnhandler<'w when 'w :> 'w EventSystem> =
    'w EventSystem -> 'w EventSystem

[<RequireQualifiedAccess>]
module EventSystem =

    let private getEventSystemDelegate<'w when 'w :> 'w EventSystem> (world : 'w) =
        world.GetEventSystemDelegateHook ()
        
    let private getEventSystemDelegateBy<'a, 'w when 'w :> 'w EventSystem> (by : 'w EventSystemDelegate -> 'a) (world : 'w) : 'a =
        let propertySystem = world.GetEventSystemDelegateHook ()
        by propertySystem
        
    let private updateEventSystemDelegate<'w when 'w :> 'w EventSystem> updater (world : 'w) =
        world.UpdateEventSystemDelegateHook updater

    /// Get event subscriptions.
    let getSubscriptions<'w when 'w :> 'w EventSystem> (world : 'w) =
        getEventSystemDelegateBy EventSystemDelegate.getSubscriptions world

    /// Get event unsubscriptions.
    let getUnsubscriptions<'w when 'w :> 'w EventSystem> (world : 'w) =
        getEventSystemDelegateBy EventSystemDelegate.getUnsubscriptions world

    /// Set event subscriptions.
    let setSubscriptions<'w when 'w :> 'w EventSystem> subscriptions (world : 'w) =
        updateEventSystemDelegate (EventSystemDelegate.setSubscriptions subscriptions) world

    /// Set event unsubscriptions.
    let setUnsubscriptions<'w when 'w :> 'w EventSystem> unsubscriptions (world : 'w) =
        updateEventSystemDelegate (EventSystemDelegate.setUnsubscriptions unsubscriptions) world

    /// Add event state to the world.
    let addEventState<'a, 'w when 'w :> 'w EventSystem> key (state : 'a) (world : 'w) =
        updateEventSystemDelegate (EventSystemDelegate.addEventState key state) world

    /// Remove event state from the world.
    let removeEventState<'w when 'w :> 'w EventSystem> key (world : 'w) =
        updateEventSystemDelegate (EventSystemDelegate.removeEventState key) world

    /// Get event state from the world.
    let getEventState<'a, 'g ,'w when 'w :> 'w EventSystem> key (world : 'w) : 'a =
        getEventSystemDelegateBy (EventSystemDelegate.getEventState<'a, 'w> key) world

    /// Get how events are being traced.
    let getEventTracerOpt<'w when 'w :> 'w EventSystem> (world : 'w) =
        getEventSystemDelegateBy EventSystemDelegate.getEventTracerOpt<'w> world

    /// Set how events are being traced.
    let setEventTracerOpt<'w when 'w :> 'w EventSystem> tracerOpt (world : 'w) =
        updateEventSystemDelegate (EventSystemDelegate.setEventTracerOpt tracerOpt) world

    /// Get the state of the event filter.
    let getEventFilter<'w when 'w :> 'w EventSystem> (world : 'w) =
        getEventSystemDelegateBy EventSystemDelegate.getEventFilter world

    /// Set the state of the event filter.
    let setEventFilter<'w when 'w :> 'w EventSystem> filter (world : 'w) =
        updateEventSystemDelegate (EventSystemDelegate.setEventFilter filter) world

    let boxCallback<'a, 's, 'w when 's :> Simulant and 'w :> 'w EventSystem>
        (callback : Callback<'a, 's, 'w>) =
        let boxableCallback = fun (evt : Event<obj, Simulant>) world ->
            let evt =
                { Data = evt.Data :?> 'a
                  Subscriber = evt.Subscriber :?> 's
                  Publisher = evt.Publisher
                  Address = Address.specialize<'a> evt.Address
                  Trace = evt.Trace }
            callback evt world
        boxableCallback :> obj

    let getSortableSubscriptions
        (getSortPriority : Simulant -> 'w -> IComparable)
        (subscriptions : SubscriptionEntry array)
        (world : 'w) :
        (IComparable * SubscriptionEntry) array =
        Array.map
            (fun (subscription : SubscriptionEntry) ->
                // NOTE: we just take the sort priority of the first callback found when callbacks are compressed. This
                // is semantically sub-optimal, but should be fine for all of our cases.
                let priority = getSortPriority (Triple.snd subscription.Callbacks.[0]) world
                (priority, subscription))
            subscriptions

    let getLiveness<'w when 'w :> 'w EventSystem> (world : 'w) =
        world.GetLiveness ()

    let getGlobalSimulantSpecialized<'w when 'w :> 'w EventSystem> (world : 'w) =
        world.GetGlobalSimulantSpecialized ()

    let getGlobalSimulantGeneralized<'w when 'w :> 'w EventSystem> (world : 'w) =
        world.GetGlobalSimulantGeneralized ()

    let simulantExists<'w when 'w :> 'w EventSystem> (simulant : Simulant) (world : 'w) =
        world.SimulantExists simulant

    /// Publish an event directly.
    let publishEvent<'a, 'p, 's, 'w when 'p :> Simulant and 's :> Simulant and 'w :> 'w EventSystem>
        (subscriber : Simulant) (publisher : 'p) (eventData : obj) (eventAddress : 'a Address) eventTrace (subscription : obj) (world : 'w) =
        let evt =
            { Data = eventData
              Subscriber = subscriber
              Publisher = publisher :> Simulant
              Address = atooa eventAddress
              Trace = eventTrace }
        let callableSubscription = subscription :?> 'w BoxableSubscription
        callableSubscription evt world

    /// Sort subscriptions using categorization via the 'by' procedure.
    let sortSubscriptionsBy by (subscriptions : SubscriptionEntry array) (world : 'w) =
        let subscriptions = getSortableSubscriptions by subscriptions world
        let subscriptions = Array.sortWith (fun ((p : IComparable), _) ((p2 : IComparable), _) -> p.CompareTo p2) subscriptions
        Array.map snd subscriptions

    /// A 'no-op' for subscription sorting - that is, performs no sorting at all.
    let sortSubscriptionsNone (subscriptions : SubscriptionEntry array) (_ : 'w) =
        subscriptions

    /// Publish an event, using the given publishSorter procedures to arrange the order to which subscriptions are published.
    let publishPlus<'a, 'p, 'w when 'p :> Simulant and 'w :> 'w EventSystem>
        (eventData : 'a)
        (eventAddress : 'a Address)
        eventTrace
        (publisher : 'p)
        (sorterOpt : SubscriptionSorter option)
        (world : 'w) =
        let eventAddressObj = atooa eventAddress
#if DEBUG
        EventSystemDelegate.logEvent<'w> eventAddressObj eventTrace (getEventSystemDelegate world)
#endif
        let subscriptions =
            match sorterOpt with
            | Some sorter ->
                EventSystemDelegate.getSubscriptionsSorted sorter eventAddressObj (getEventSystemDelegate world) world
            | None ->
                let subscriptions = EventSystemDelegate.getSubscriptions (getEventSystemDelegate world)
                match UMap.tryFind eventAddressObj subscriptions with Some subs -> subs | None -> [||]
        let (_, world) =
            Array.foldWhile
                (fun (handling, world : 'w) (subscription : SubscriptionEntry) ->
                    if handling = Cascade && world.GetLiveness () = Running then
                        let mapped =
                            match subscription.MapperOpt with
                            | Some mapper -> mapper eventData subscription.PreviousDataOpt world
                            | None -> eventData :> obj
                        let filtered =
                            match subscription.FilterOpt with
                            | Some filter -> filter mapped subscription.PreviousDataOpt world
                            | None -> true
                        subscription.PreviousDataOpt <- Some mapped
                        let (handling, world) =
                            if filtered then
                                Array.fold (fun (handling, world : 'w) (_, subscriber, callback) ->
                                    match handling with
                                    | Cascade ->
                                        match callback with
                                        | UserDefinedCallback callback -> world.HandleUserDefinedCallback callback mapped world
                                        | FunctionCallback callback -> world.PublishEventHook subscriber publisher mapped eventAddress eventTrace callback world
                                    | Resolve -> (handling, world))
                                    (handling, world)
                                    subscription.Callbacks
                            else (Cascade, world)
                        Some (handling, world)
                    else None)
                (Cascade, world)
                subscriptions
        world

    /// Publish an event with no subscription sorting.
    let publish<'a, 'p, 'w when 'p :> Simulant and 'w :> 'w EventSystem>
        (eventData : 'a) (eventAddress : 'a Address) eventTrace (publisher : 'p) (world : 'w) =
        publishPlus<'a, 'p, 'w> eventData eventAddress eventTrace publisher None world

    /// Unsubscribe from an event.
    let unsubscribe<'w when 'w :> 'w EventSystem> subscriptionKey (world : 'w) =
        let (subscriptions, unsubscriptions) = (getSubscriptions world, getUnsubscriptions world)
        match UMap.tryFind subscriptionKey unsubscriptions with
        | Some (eventAddress, _) ->
            match UMap.tryFind eventAddress subscriptions with
            | Some subscriptionEntries ->
                let subscriptionEntryOpt =
                    Array.tryFind (fun (subscriptionEntry : SubscriptionEntry) ->
                        subscriptionEntry.SubscriptionKey = subscriptionKey)
                        subscriptionEntries
                let subscriptionEntryOpt =
                    match subscriptionEntryOpt with
                    | Some subscriptionEntry ->
                        let callbacks = Array.remove (fun (key, _, _) -> key = subscriptionKey) subscriptionEntry.Callbacks
                        if Array.notEmpty callbacks then Some (Some { subscriptionEntry with Callbacks = callbacks })
                        else Some None
                    | None -> None
                let subscriptions =
                    match subscriptionEntryOpt with
                    | Some (Some subscriptionEntry) ->
                        let subscriptionEntries =
                            Array.replace (fun (subscriptionEntry : SubscriptionEntry) ->
                                subscriptionEntry.SubscriptionKey = subscriptionKey)
                                subscriptionEntry
                                subscriptionEntries
                        UMap.add eventAddress subscriptionEntries subscriptions
                    | Some None ->
                        let subscriptionEntries =
                            Array.remove (fun subscription ->
                                subscription.SubscriptionKey = subscriptionKey)
                                subscriptionEntries
                        if Array.isEmpty subscriptionEntries
                        then UMap.remove eventAddress subscriptions
                        else UMap.add eventAddress subscriptionEntries subscriptions
                    | None -> subscriptions
                let unsubscriptions = UMap.remove subscriptionKey unsubscriptions
                let world = setSubscriptions subscriptions world
                let world = setUnsubscriptions unsubscriptions world
                let world =
                    publish<_, _, 'w>
                        eventAddress
                        (rtoa<obj Address> [|"Unsubscribe"; "Event"|])
                        (EventTrace.record "EventSystem" "unsubscribe" EventTrace.empty)
                        (getGlobalSimulantSpecialized world)
                        world
                world
            | None -> world
        | None -> world

    /// Subscribe to an event using the given subscriptionKey, and be provided with an unsubscription callback.
    let subscribeSpecial<'a, 'b, 's, 'w when 's :> Simulant and 'w :> 'w EventSystem>
        (subscriptionKey : Guid)
        (compressionId : Guid)
        (mapperOpt : ('a -> 'b option -> 'w -> 'b) option)
        (filterOpt : ('b -> 'b option -> 'w -> bool) option)
        (stateOpt : 'b option)
        (callback : Either<Callback<'b, 's, 'w>, obj>)
        (eventAddress : 'a Address)
        (subscriber : 's)
        (world : 'w) =
        if not (Address.isEmpty eventAddress) then
            let eventAddressObj = atooa eventAddress
            let callback = match callback with Left s ->  s |> boxCallback |> FunctionCallback | Right o -> UserDefinedCallback o
            let (subscriptions, unsubscriptions) = (getSubscriptions world, getUnsubscriptions world)
            let subscriptions =
                match UMap.tryFind eventAddressObj subscriptions with
                | Some subscriptionEntries ->
                    let compressedSubscriptionEntryOpt =
                        Array.tryFind (fun entry ->
                            entry.CompressionId = compressionId)
                            subscriptionEntries
                    match compressedSubscriptionEntryOpt with
                    | Some subscriptionEntry ->
                        let callbacks = Array.add (subscriptionKey, subscriber :> Simulant, callback) subscriptionEntry.Callbacks
                        let subscriptionEntry = { subscriptionEntry with Callbacks = callbacks }
                        let subscriptionEntries =
                            // NOTE: using replace here does potentially put callbacks in an order other than which
                            // they are received. This is semantically suboptimal, but necessary for their performance
                            // boost.
                            Array.replace (fun subscriptionEntry ->
                                subscriptionEntry.CompressionId = compressionId)
                                subscriptionEntry
                                subscriptionEntries
                        UMap.add eventAddressObj subscriptionEntries subscriptions
                    | None ->
                        let subscriptionEntry =
                            { SubscriptionKey = subscriptionKey
                              CompressionId = compressionId
                              MapperOpt = Option.map (fun mapper -> fun a p w -> mapper (a :?> 'a) (Option.map cast<'b> p) (w :?> 'w) :> obj) mapperOpt
                              FilterOpt = Option.map (fun filter -> fun b p w -> filter (b :?> 'b) (Option.map cast<'b> p) (w :?> 'w)) filterOpt
                              PreviousDataOpt = Option.map box stateOpt
                              Callbacks = [|(subscriptionKey, subscriber :> Simulant, callback)|] }
                        let subscriptionEntries = Array.add subscriptionEntry subscriptionEntries
                        UMap.add eventAddressObj subscriptionEntries subscriptions
                | None ->
                    let subscriptionEntry =
                        { SubscriptionKey = subscriptionKey
                          CompressionId = compressionId
                          MapperOpt = Option.map (fun mapper -> fun a p w -> mapper (a :?> 'a) (Option.map cast<'b> p) (w :?> 'w) :> obj) mapperOpt
                          FilterOpt = Option.map (fun filter -> fun b p w -> filter (b :?> 'b) (Option.map cast<'b> p) (w :?> 'w)) filterOpt
                          PreviousDataOpt = Option.map box stateOpt
                          Callbacks = [|(subscriptionKey, subscriber :> Simulant, callback)|] }
                    UMap.add eventAddressObj [|subscriptionEntry|] subscriptions
            let unsubscriptions = UMap.add subscriptionKey (eventAddressObj, subscriber :> Simulant) unsubscriptions
            let world = setSubscriptions subscriptions world
            let world = setUnsubscriptions unsubscriptions world
            let world =
                publish
                    eventAddressObj
                    (rtoa<obj Address> [|"Subscribe"; "Event"|])
                    (EventTrace.record "EventSystem" "subscribePlus5" EventTrace.empty)
                    (getGlobalSimulantSpecialized world)
                    world
            (unsubscribe<'w> subscriptionKey, world)
        else failwith "Event name cannot be empty."

    /// Subscribe to an event.
    let subscribePlus<'a, 'b, 's, 'w when 's :> Simulant and 'w :> 'w EventSystem>
        (subscriptionKey : Guid)
        (mapperOpt : ('a -> 'b option -> 'w -> 'b) option)
        (filterOpt : ('b -> 'b option -> 'w -> bool) option)
        (stateOpt : 'b option)
        (callback : Callback<'b, 's, 'w>)
        (eventAddress : 'a Address)
        (subscriber : 's)
        (world : 'w) =
        subscribeSpecial subscriptionKey (makeGuid ()) mapperOpt filterOpt stateOpt (Left callback) eventAddress subscriber world

    /// Subscribe to an event.
    let subscribe<'a, 's, 'w when 's :> Simulant and 'w :> 'w EventSystem>
        (callback : Callback<'a, 's, 'w>) (eventAddress : 'a Address) (subscriber : 's) world =
        subscribePlus (makeGuid ()) None None None callback eventAddress subscriber world |> snd

    /// Keep active a subscription for the life span of a simulant, and be provided with an unsubscription callback.
    let monitorSpecial<'a, 'b, 's, 'w when 's :> Simulant and 'w :> 'w EventSystem>
        (compressionId : Guid)
        (mapperOpt : ('a -> 'b option -> 'w -> 'b) option)
        (filterOpt : ('b -> 'b option -> 'w -> bool) option)
        (stateOpt : 'b option)
        (callback : Either<Callback<'b, 's, 'w>, obj>)
        (eventAddress : 'a Address)
        (subscriber : 's)
        (world : 'w) =
        let monitorKey = makeGuid ()
        let removalKey = makeGuid ()
        let world = subscribeSpecial<'a, 'b, 's, 'w> monitorKey compressionId mapperOpt filterOpt stateOpt callback eventAddress subscriber world |> snd
        let unsubscribe = fun (world : 'w) ->
            let world = unsubscribe removalKey world
            let world = unsubscribe monitorKey world
            world
        let callback' = Left (fun _ eventSystem -> (Cascade, unsubscribe eventSystem))
        let removingEventAddress = rtoa<obj> [|"Unregistering"; "Event"|] --> subscriber.SimulantAddress
        let world = subscribeSpecial<obj, obj, Simulant, 'w> removalKey (makeGuid ()) None None None callback' removingEventAddress subscriber world |> snd
        (unsubscribe, world)

    /// Keep active a subscription for the life span of a simulant, and be provided with an unsubscription callback.
    let monitorPlus<'a, 'b, 's, 'w when 's :> Simulant and 'w :> 'w EventSystem>
        (mapperOpt : ('a -> 'b option -> 'w -> 'b) option)
        (filterOpt : ('b -> 'b option -> 'w -> bool) option)
        (stateOpt : 'b option)
        (callback : Callback<'b, 's, 'w>)
        (eventAddress : 'a Address)
        (subscriber : 's)
        (world : 'w) =
        monitorSpecial<'a, 'b, 's, 'w> (makeGuid ()) mapperOpt filterOpt stateOpt (Left callback) eventAddress subscriber world

    /// Keep active a subscription for the life span of a simulant.
    let monitor<'a, 's, 'w when 's :> Simulant and 'w :> 'w EventSystem>
        (callback : Callback<'a, 's, 'w>) (eventAddress : 'a Address) (subscriber : 's) (world : 'w) =
        monitorPlus<'a, 'a, 's, 'w> None None None callback eventAddress subscriber world |> snd