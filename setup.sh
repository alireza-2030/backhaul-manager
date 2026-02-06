#!/bin/bash

# Colors
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[0;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Default values
DEFAULT_LOCAL_PORT=3001
CONFIG_FILE="/usr/local/etc/xray/config.json"

echo -e "${GREEN}============================================${NC}"
echo -e "${GREEN}      Xray Tunnel Setup (Dynamic Config)    ${NC}"
echo -e "${GREEN}============================================${NC}"

# Function to urldecode
urldecode() { : "${*//+/ }"; echo -e "${_//%/\\x}"; }

# 1. Get Inputs
echo -e "${YELLOW}Please paste your VLESS Reality URL:${NC}"
read -r VLESS_URL

if [[ ! "$VLESS_URL" =~ ^vless:// ]]; then
    echo -e "${RED}Error: Invalid VLESS URL! Must start with vless://${NC}"
    exit 1
fi

echo -e "${YELLOW}Enter Local Tunnel Port (The port on THIS server to forward traffic from) [Default: 3001]:${NC}"
read -r LOCAL_PORT
LOCAL_PORT=${LOCAL_PORT:-$DEFAULT_LOCAL_PORT}

# 2. Parse VLESS URL
echo -e "${BLUE}Parsing VLESS URL...${NC}"

# Remove 'vless://'
REST="${VLESS_URL#*://}"

# Extract UUID
UUID="${REST%%@*}"
REST="${REST#*@}"

# Split into Address:Port and the rest (Query/Fragment)
if [[ "$REST" == *"\?"* ]]; then
    ADDRESS_PORT="${REST%%\?*}"
    QUERY_FRAGMENT="${REST#*\?}"
else
    # No query params, check for fragment
    ADDRESS_PORT="${REST%%\#*}"
    QUERY_FRAGMENT=""
    if [[ "$REST" == *"\#"* ]]; then
        QUERY_FRAGMENT="#${REST#*\#}"
    fi
fi

# Extract Address and Port
ADDRESS="${ADDRESS_PORT%%:*}"
PORT="${ADDRESS_PORT##*:}"

# Clean URL from fragment if present in Query section
QUERY="${QUERY_FRAGMENT%%\#*}"
REMARK=""
if [[ "$QUERY_FRAGMENT" == *"\#"* ]]; then
    REMARK="${QUERY_FRAGMENT##*\#}"
    REMARK=$(urldecode "$REMARK")
fi

# Parse Query Params
get_param() {
    local param_name=$1
    if [ -z "$QUERY" ]; then echo ""; return; fi
    # Use grep if available, else simplified sed
    if echo "test" | grep -P "t" &>/dev/null; then
         echo "$QUERY" | grep -oP "(?<=$param_name=)[^&]*"
    else
         echo "$QUERY" | sed -n "s/.*$param_name=\([^&]*\).*/\1/p"
    fi
}

FLOW=$(get_param "flow")
SECURITY=$(get_param "security")
SNI=$(get_param "sni")
FP=$(get_param "fp")
PBK=$(get_param "pbk")
SID=$(get_param "sid")
SPX=$(get_param "spx")
SPX=$(urldecode "$SPX")
TYPE=$(get_param "type")

# Defaults if missing
[ -z "$SECURITY" ] && SECURITY="none"
[ -z "$TYPE" ] && TYPE="tcp"
[ -z "$FLOW" ] && FLOW=""

echo -e "UUID: ${GREEN}$UUID${NC}"
echo -e "Address: ${GREEN}$ADDRESS${NC}"
echo -e "Port: ${GREEN}$PORT${NC}"
echo -e "Security: ${GREEN}$SECURITY${NC}"
echo -e "Flow: ${GREEN}$FLOW${NC}"
echo -e "SNI: ${GREEN}$SNI${NC}"
echo -e "PBK: ${GREEN}$PBK${NC}"
echo -e "SID: ${GREEN}$SID${NC}"
echo -e "Local Port: ${GREEN}$LOCAL_PORT${NC}"

# 3. Request Destination Info (Implicitly 127.0.0.1:LOCAL_PORT based on request, but let's confirm logic)
# User asked: "tunnel port that we want... listen on 3001 and send to outside" -> IMPLIED destination is 127.0.0.1:3001 on the other side.
DEST_IP="127.0.0.1"
DEST_PORT="$LOCAL_PORT"

# 4. Install Xray
if ! command -v xray &> /dev/null; then
    echo -e "${BLUE}Installing Xray-core...${NC}"
    bash -c "$(curl -L https://github.com/XTLS/Xray-install/raw/main/install-release.sh)" @ install
else
    echo -e "${GREEN}Xray-core is already installed.${NC}"
fi

# 5. Generate Config
echo -e "${BLUE}Generating config.json...${NC}"
mkdir -p /usr/local/etc/xray

cat > "$CONFIG_FILE" <<EOF
{
  "log": {
    "loglevel": "warning"
  },
  "inbounds": [
    {
      "tag": "tunnel-in",
      "port": $LOCAL_PORT,
      "protocol": "dokodemo-door",
      "settings": {
        "address": "$DEST_IP",
        "port": $DEST_PORT,
        "network": "tcp,udp"
      }
    }
  ],
  "outbounds": [
    {
      "tag": "proxy",
      "protocol": "vless",
      "settings": {
        "vnext": [
          {
            "address": "$ADDRESS",
            "port": $PORT,
            "users": [
              {
                "id": "$UUID",
                "encryption": "none",
                "flow": "$FLOW"
              }
            ]
          }
        ]
      },
      "streamSettings": {
        "network": "$TYPE",
        "security": "$SECURITY",
        "realitySettings": {
          "show": false,
          "fingerprint": "$FP",
          "serverName": "$SNI",
          "publicKey": "$PBK",
          "shortId": "$SID",
          "spiderX": "$SPX"
        }
      }
    },
    {
      "tag": "direct",
      "protocol": "freedom",
      "settings": {}
    },
    {
      "tag": "block",
      "protocol": "blackhole",
      "settings": {
        "response": {
          "type": "http"
        }
      }
    }
  ],
  "routing": {
    "domainStrategy": "AsIs",
    "rules": [
      {
        "type": "field",
        "inboundTag": ["tunnel-in"],
        "outboundTag": "proxy"
      }
    ]
  }
}
EOF

# 6. Restart Service
echo -e "${BLUE}Restarting Xray service...${NC}"
systemctl daemon-reload
systemctl enable xray
systemctl restart xray

# 7. Check Status
if systemctl is-active --quiet xray; then
    echo -e "${GREEN}Success! Xray Tunnel is RUNNING.${NC}"
    echo -e "Listening on Iran Server Port: ${YELLOW}$LOCAL_PORT${NC}"
    echo -e "Forwarding to Foreign Server: ${YELLOW}$DEST_IP:$DEST_PORT${NC} (via Tunnel)"
else
    echo -e "${RED}Failed to start Xray tunnel.${NC}"
    systemctl status xray --no-pager
fi
