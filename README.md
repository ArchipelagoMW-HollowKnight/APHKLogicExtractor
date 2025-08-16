# APHKLogicExtractor

Command line application for extracting RandomizerCore logic to Archipelago structures. It processes multiple component jobs in parallel
to generate region definitions, item effects, and related data.

## Command Line Arguments

- `--Input` (required): Path to the input configuration file in JSON format
- `--Output`: Directory path for output files (default: `./output`)
- `--Jobs`: Space-separated list of jobs to run. Valid values: `ExtractItems`, `ExtractRegions`, `ExtractData`. When 
  omitted, all jobs will run
- `--IgnoreCache`: When true, bypasses the resource cache and forces fresh downloads/reads
- `--Bundle`: When true, bundles the output folder into a zip file

## Input Configuration

The input configuration file specified by `--Input` contains settings for the extraction process. It uses a flexible resource system that supports:
- Absolute or relative file paths (e.g. `./path/to/file.json` or `C:/path/to/file.json`)
- HTTP/HTTPS URLs (e.g. `https://example.com/data.json`)
- Raw JSON content inline

The configuration has the following structure:
```json
{
  "Type": "JsonLogic | RandoContext | WorldDefinition",
  "Configuration": {
    // Logic configuration content or reference
  },
  "StartStateTerm": "optional term to use as menu region",
  "ClassifierModel": {
    // State modifier classification rules or reference 
  },
  "EmptyRegionsToKeep": [
    // List of region names to preserve or reference
  ],
  "IgnoredTerms": [
    // List of terms to ignore in logic or reference
  ],
  "IgnoredItems": [
    // List of items to ignore or reference
  ]
}
```

### Configuration Properties

- `Type`: **Required.** Determines the expected format of the `Configuration` field:
  - `JsonLogic`: Expects a HK-style JSON logic definition. The configuration should be a JSON object or reference
    containing logic, items, terms, waypoints, locations, transitions, etc. This is generally intended for HK use only.
  - `RandoContext`: Expects a serialized RandomizerCore RandoContext object. The configuration should be a JSON object
    or reference containing a saved RandoContext. Any RandomizerCore based game which can serialize to JSON can use
	this input format.
  - `WorldDefinition`: Expects a custom string-based world definition. The configuration should be a JSON object or 
    reference containing a list of logic objects, each with a name, handling type, and logic clauses. This is the most
	generalized input format and can be used by any consumer.
- `Configuration`: **Required.** The format and content of this field is determined by the value of `Type`:
  - For `JsonLogic`, provide a logic configuration as described above.
  - For `RandoContext`, provide a serialized RandoContext.
  - For `WorldDefinition`, provide a `StringWorldDefinition` object. This should be a JSON object with the following structure:
    ```json
    {
      "LogicObjects": [
        {
          "Name": "RegionOrLocationName",
          "Handling": "Default | Location | Transition",
          "IsEvent": true,
          "Clauses": [
            {
              "StateProvider": "optional state term",
              "Conditions": ["Term1", "Term2"],
              "StateModifiers": ["$BENCHRESET", "$TAKEDAMAGE"]
            }
          ]
        }
      ]
    }
    ```    
    - **Name**: Unique string identifier for the region, location, or transition.
    - **Handling**: Specifies how the object is treated (`Default` for regions/stateful waypoints, `Location` for
	  locations/events/stateless waypoints, `Transition` for transitions).
    - **IsEvent**: Boolean indicating if the object is an event.
    - **Clauses**: List of logic clauses in Disjunctive Normal Form (DNF). Each clause may specify a state provider, a set of conditions, and an ordered list of state modifiers.
	  - Each entry in the Clauses list acts as a conjunction (AND), while the Clauses list itself acts as a disjunction (OR)
	  - A state provider is essentially the "from" if the logic object is the "to". During the initial conversion, it will
	    act as the parent region, but may be optimized out later. Generally, it should correspond to a transition or default
		logic object.
	  - A condition is analagous to a required item or event effect. A condition prefixed with `*` will be interpreted as
	    a location requirement. A condition suffixed with `/` will be interpreted as a region requirement.
	  - A state modifier is an advanced concept used in RandomizerCore. Most users are not likely to need this field.

- `StartStateTerm`: Optional. When specified, this term becomes the menu region.
- `ClassifierModel`: Optional. Rules for classifying state modifiers as beneficial or detrimental. See [State Classification](#state-classification) below.
- `EmptyRegionsToKeep`: Optional. List of region names that should not be removed during optimization.
- `IgnoredTerms`: Optional. List of terms to exclude from item effect processing.
- `IgnoredItems`: Optional. List of items to exclude from processing

### State Classification

State modifiers can be classified to optimize the processing of [state logic](https://homothetyhk.github.io/RandomizerCore/articles/state.html).
The classification model defines which modifiers are:
- Beneficial (e.g., gaining soul, bench rests)
- Detrimental (e.g., taking damage, spending soul)  
- Mixed effects (improving some states while worsening others)


See [hkStateConfig.json](https://github.com/ArchipelagoMW-HollowKnight/APHKLogicExtractor/blob/master/APHKLogicExtractor/hkStateConfig.json) for a complete example.

## Output

The extractor generates several files containing:
- Region definitions and connections
- Location and transition logic
- Item effects and requirements
- Constants and enums
- A GraphViz visualization of the region graph