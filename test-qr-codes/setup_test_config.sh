#!/bin/bash
# Setup SmartLog Scanner with test HMAC secret

HMAC_SECRET="smartlog-test-secret-2026"
CONFIG_DIR="$HOME/Library/Containers/com.smartlog.scanner/Data/Library/Preferences"
CONFIG_FILE="$CONFIG_DIR/config.json"

echo "Setting up SmartLog Scanner test configuration..."
echo ""

# Create config directory if it doesn't exist
if [ ! -d "$CONFIG_DIR" ]; then
    echo "⚠️  App container not found yet"
    echo "   Run the app once to create the container, then run this script again"
    echo ""
    echo "OR manually configure in the app:"
    echo "   1. Open SmartLog Scanner"
    echo "   2. Click Settings (⚙️)"
    echo "   3. Enter HMAC Secret: $HMAC_SECRET"
    echo "   4. Save configuration"
    exit 0
fi

# Create or update config file
mkdir -p "$CONFIG_DIR"

cat > "$CONFIG_FILE" << JSON
{
  "ServerUrl": "http://localhost:7001",
  "ApiKey": "test-api-key-12345",
  "HmacSecret": "$HMAC_SECRET",
  "DeviceId": "TEST-SCANNER-001",
  "DeviceName": "Test Scanner - POC",
  "Scanner": {
    "Mode": "Camera",
    "DefaultScanType": "ENTRY"
  }
}
JSON

if [ $? -eq 0 ]; then
    echo "✓ Configuration saved to:"
    echo "  $CONFIG_FILE"
    echo ""
    echo "Configuration:"
    echo "  HMAC Secret: $HMAC_SECRET"
    echo "  Server URL: http://localhost:7001"
    echo "  Scanner Mode: Camera"
    echo "  Scan Type: ENTRY"
    echo ""
    echo "✓ Ready to scan test QR codes!"
else
    echo "✗ Failed to write configuration"
    exit 1
fi
