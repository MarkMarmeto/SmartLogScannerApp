#!/bin/bash

# SmartLog Scanner - Test Configuration Setup
# Updated with new HMAC secret: test-hmac-qr-code-2026

CONFIG_DIR="$HOME/Library/Containers/com.smartlog.scanner/Data/Library/Preferences"
CONFIG_FILE="$CONFIG_DIR/config.json"

echo "🔧 Setting up SmartLog Scanner test configuration..."
echo ""

# Create directory if it doesn't exist
mkdir -p "$CONFIG_DIR"

# Create configuration file
cat > "$CONFIG_FILE" << 'EOF'
{
  "ServerUrl": "http://localhost:7001",
  "ApiKey": "test-api-key-12345",
  "HmacSecret": "test-hmac-qr-code-2026",
  "ScanType": "ENTRY",
  "IsOfflineModeEnabled": false
}
EOF

# Verify configuration was created
if [ -f "$CONFIG_FILE" ]; then
    echo "✅ Configuration created successfully!"
    echo ""
    echo "📋 Configuration:"
    cat "$CONFIG_FILE" | python3 -m json.tool
    echo ""
    echo "🎯 Next Steps:"
    echo "1. Run the app: cd ~/Projects/SmartLogScannerApp && dotnet run --project SmartLog.Scanner/SmartLog.Scanner.csproj --framework net8.0-maccatalyst"
    echo "2. Open test QR codes: open /tmp/test_qr_codes_new.html"
    echo "3. Scan QR codes and verify student names appear!"
    echo ""
    echo "🔍 Expected Results:"
    echo "   First scan (Juan):  ✓ Juan Dela Cruz - Grade 11 A (GREEN feedback)"
    echo "   Duplicate (< 30s): ⚠ Juan Dela Cruz already scanned (AMBER feedback)"
    echo "   Different student: ✓ Maria Santos - Grade 11 B (GREEN feedback)"
else
    echo "❌ Failed to create configuration file"
    exit 1
fi
