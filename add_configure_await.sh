#!/bin/bash

# Script to add ConfigureAwait(false) to all await calls in library code
# This ensures library code doesn't capture the synchronization context

# Find all .cs files in src/ directory (excluding CLI project which is the entry point)
find src/ -name "*.cs" -not -path "*/Dotp2pNet.CLI/*" | while read file; do
    # Add ConfigureAwait(false) to await calls that don't already have it
    # This regex looks for "await " followed by something that ends with ")" but not ".ConfigureAwait"
    sed -i.bak -E 's/await ([^;]+)\);/await \1).ConfigureAwait(false);/g' "$file"
    
    # Clean up if no changes were made
    if diff "$file" "$file.bak" > /dev/null 2>&1; then
        rm "$file.bak"
    else
        echo "Updated: $file"
        rm "$file.bak"
    fi
done

echo "ConfigureAwait(false) additions complete"
