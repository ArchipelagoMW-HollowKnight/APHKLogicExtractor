# APHKLogicExtractor

Command line application for extracting RandomizerCore logic to Archipelago structures. It is comprised of multiple
component jobs which run in parallel.

* [General command line arguments](#general-command-line-arguments)
* [Region extractor](#region-extractor)
    + [Loading HK logic from upstream](#loading-hk-logic-from-upstream)
    + [Loading RandomizerCore logic from a locally serialized RandoContext](#loading-randomizercore-logic-from-a-locally-serialized-randocontext)
    + [Loading non-RandomizerCore logic from a local world definition](#loading-non-randomizercore-logic-from-a-local-world-definition)
    + [Modifying reduction of state modifiers](#modifying-reduction-of-state-modifiers)

## General command line arguments

The command line argument `--Output` can be used to specify a folder path to place output files. The default is `./output`.

## Region extractor

The region extractor is able to take string logic as an input (a variety of formats are possible) and outputs a JSON
file with all the information necessary to construct Archipelago regions and locations. The output contains:

* Regions - a list of regions to create. Each region has the following properties:
    * Name - The name of the region. Menu is included
    * Exits - A list of exits from the region. Each exit contains:
        * Target - the name of the target region
        * Logic - a list of logic requirements (explained below).
    * Locations - A list of location names which appear in the region
    * Transitions - A list of randomizable transition names which appear in the region
* Locations - A list of locations to create. Event locations may be included. Each location has the following properties:
    * Name - The name of the location
    * Logic - a list of logic requirements (explained below)
* Transitions - A list logic for randomizable transitions (i.e. edges not included in the region graph by default which need
  to be added in order to complete the graph, or randomize for ER). Note also that special care should be taken for one-way
  entrances; a transition may appear in this list even if it cannot be used as an exit, and this data alone must be supplemented
  in order to determine the correct handling. Each transition has the following properties:
    * Name - the name of the transition
    * Logic - a list of logic requirements (explained below)

The extractor job also generates a GraphViz dot file which can be used to visualize the created region graph (you'll want to export
as svg to prevent compression).

Logic requirements are always presented as a list of alternatives. In other words, in Archipelago, after modeling each requirement
object, the access rule for an Entrance/Location can be checked as `all(lambda req: req.satisfied(state), logic)`. Each requirement
contains a set of item requirements (`state.has(item, player)`, possibly with a count for comparisons), a set of location requirements
(`state.can_reach(location, resolution_hint="Location", player=player)`), and an ordered list of state modifiers to apply. For more
information on state logic, see the [RandomizerCore documentation](https://homothetyhk.github.io/RandomizerCore/articles/state.html).

### Keeping empty regions for organizational purposes

By default, the extractor will attempt to remove any redundant empty regions. If maintaining certain regions is desirable for
organization (such as HK's Can_Bench and Can_Stag regions which have many entrances and exits which would be expensive to distribute),
the `--EmptyRegionsToKeepPath` argument can be used. This should be a path to a JSON file containing a list of strings (region names)
which should not be removed during this process. You can see an example of this for HK 
[here](https://github.com/ArchipelagoMW-HollowKnight/APHKLogicExtractor/blob/master/APHKLogicExtractor/hkEmptyRegionsToKeep.json)

### Loading HK logic from upstream

By default, when the extractor is run with no command line arguments, logic will be automatically pulled from the RandomizerMod repository
on the main branch. If needed, a specific git ref can be set with the argument `--RefName`.

### Loading RandomizerCore logic from a locally serialized RandoContext

Logic can be loaded from a locally serialized RandoContext object created by the RandomizerCore.Json library. This can be
used for any RandomizerCore randomizer, including Hollow Knight with a non-default LogicManager (e.g. with connections enabled).
Specify the path to the JSON file with the argument `--RandoContextPath`. To ensure good results, warnings for ambiguous, missing,
or poorly ordered state providers should be resolved.

### Loading non-RandomizerCore logic from a local world definition

Logic can be loaded from a locally serialized `StringWorldDefinition`, which contains a list of string logic objects. This
approach allows other non-graph-based randomizers to leverage the region construction logic with only minimal preprocessing.

Locations, Waypoints/Events, and Transitions should all appear in the input list. Each input should contain the following properties:
* Name - a string representing the name of the object. Names should be unique.
* Handling - one of "Default", "Location", or "Transition"
    * Default handling will create the object as a region only. This is useful for state-transmitting waypoints.
    * Location handling will create the object as a region, and a location contained within that region. This is useful for
      stateless waypoints (events with saved effects) or actual locations.
    * Transition handling will create the object as a region, and create a record of a randomizable transition there.
* Logic - a list of state-aware logic clauses after expanding logic to Disjunctive Normal Form (DNF). Each token should be parseable
  as a RandomizerCore TermToken. For most games this will not be an issue as long as [safe naming practices](https://homothetyhk.github.io/RandomizerCore/articles/safe_naming.html)
  are followed. Each clause should contain:
    * StateProvider (optional) - The state-providing token of the clause. If your logic system does not have a construct similar to
      RandomizerCore state, this is usually just the transition used in the logic clause. For example, in an access rule 
      `SomeRoom[left] + DoubleJump`, `SomeRoom[left1]` should be the state provider. The state provider's region will become the
      parent region before simplification. In rare cases, a StateProvider may not be present in a clause; in this case the parent
      region will be the Menu region.
    * Conditions (required, may be empty) - A set of boolean condition tokens.
    * StateModifiers (required, may be empty) - An ordered list of state modifier tokens.

### Modifying reduction of state modifiers

As part of the process of merging logic objects, redundant logic branches are removed. When state modifiers are involved, redundancy
hinges on whether a branch has "better" state modifiers than another branch, in addition to having fewer non-state requirements.
State modifiers may strictly improve state, strictly worsen state, or improve some fields and worsen some fields. By default, it is
assumed that all state modifiers are "mixed" quality, and thus logic containing state modifiers is never redundant. This can be changed
by providing the `--ClassfierModelPath` option. Without going too in-depth, this is modeled by the `StateClassificationModel` class.
You can see an example of this for HK [here](https://github.com/ArchipelagoMW-HollowKnight/APHKLogicExtractor/blob/master/APHKLogicExtractor/hkStateConfig.json).