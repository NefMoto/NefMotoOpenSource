#!/bin/env python

import re
import sys

def transform_version(version_string):
    """
    Transforms version strings based on the following rules:
    1. Removes a leading 'v' if no hyphens follow (e.g., v1.1.1.1 -> 1.1.1.1).
    2. Increments the last digit of the version number if a hyphen is present,
       keeping the leading 'v' and the suffix (e.g., v1.1.1.1-stuff -> v1.1.1.2-stuff).
    """

    # Regex breakdown for hyphenated versions:
    # ^(v)            -> Group 1: Leading 'v' (optional, but assumed present in this scenario)
    # ((\d+\.){3}\d+) -> Group 2: The full semantic version part (e.g., 1.1.1.1)
    # (\.\d+)        -> Group 4: The last dot and number (e.g., .1) which we want to increment
    # (-\w+)         -> Group 5: The suffix part (e.g., -stuff-foo)
    hyphenated_pattern = re.compile(r"^(v)((\d+\.){3})(\d+)(-.+)$")
    match = hyphenated_pattern.match(version_string)

    if match:
        # If it matches the hyphenated pattern, increment the last digit and strip v and the suffix
        prefix = match.group(2)
        last_digit = int(match.group(4))
        suffix = match.group(5)
        incremented_digit = last_digit + 1
        return f"{prefix}{incremented_digit}"
    else:
        # Otherwise, assume the standard format and just strip the 'v'
        return version_string.lstrip('v')

def main():
    # Check if exactly one command-line argument was provided (the script name is sys.argv[0])
    if len(sys.argv) != 2:
        print("Usage: python script_name.py <version_string>")
        print("Example: python script_name.py v1.1.1.1-stuff-foo")
        sys.exit(1)

    input_version = sys.argv[1]
    output_version = transform_version(input_version)
    print(output_version)

if __name__ == "__main__":
    main()
