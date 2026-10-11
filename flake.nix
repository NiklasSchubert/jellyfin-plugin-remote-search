{
  description = "Jellyfin plugin: remote search results, downloaded on play";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { nixpkgs, ... }:
    let
      systems = [ "x86_64-linux" "aarch64-linux" ];
      forAllSystems = f: nixpkgs.lib.genAttrs systems (system: f nixpkgs.legacyPackages.${system});
    in
    {
      devShells = forAllSystems (pkgs: {
        default = pkgs.mkShell {
          packages = [ pkgs.dotnetCorePackages.sdk_10_0 pkgs.jq pkgs.curl ];
          DOTNET_CLI_TELEMETRY_OPTOUT = "1";
        };
      });
    };
}
