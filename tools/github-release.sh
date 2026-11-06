#!/usr/bin/env bash
# Invoked by the release job with its verified build artifacts.
set -euo pipefail
cd dist/packages
tag="v${VERSION:?}"
assets=("AnimationComposer-$VERSION-linux-x64.tar.gz" "AnimationComposer-$VERSION-win-x64.zip" "AnimationComposer-$VERSION-source.zip" SHA256SUMS.txt)
for asset in "${assets[@]}"; do test -s "$asset"; done
sha256sum --check SHA256SUMS.txt

draft=""
if git show-ref --verify --quiet "refs/tags/$tag"; then
    if [[ "$(git rev-parse "$tag^{commit}")" != "$GITHUB_SHA" ]]; then
        echo "$tag already belongs to another commit" >&2
        exit 1
    fi
    draft=$(gh api --paginate "repos/$GITHUB_REPOSITORY/releases?per_page=100" \
        --jq ".[] | select(.tag_name == \"$tag\") | .draft")
else
    git tag "$tag" "$GITHUB_SHA"
    git push origin "refs/tags/$tag"
fi

case "$draft" in
    false)
        # A retry of a completed release must leave its downloads intact.
        published=$(gh release view "$tag" --json assets --jq '.assets[] | select(.state == "uploaded" and .size > 0) | .name')
        diff <(printf '%s\n' "${assets[@]}" | sort) <(printf '%s\n' "$published" | sort)
        echo "$tag is already published."
        ;;
    true)
        gh release upload "$tag" "${assets[@]}" --clobber
        gh release edit "$tag" --draft=false
        ;;
    "")
        gh release create "$tag" "${assets[@]}" --verify-tag --target "$GITHUB_SHA" \
            --title "Animation Composer $tag" --generate-notes \
            --notes 'Extract the Linux or Windows x64 archive and launch AnimationComposer. The source ZIP and SHA-256 checksums are included below.'
        ;;
    *)
        echo "Unexpected release state for $tag: $draft" >&2
        exit 1
        ;;
esac
