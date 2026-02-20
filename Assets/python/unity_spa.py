from flask import Flask, request, jsonify
import pandas as pd
from spa import getSolarPosition

app = Flask(__name__)

@app.route('/health')
def health():
    return jsonify({'status': 'ok'})

@app.route('/solar')
def solar():
    time_str = request.args.get('time')
    lat = float(request.args.get('lat'))
    lon = float(request.args.get('lon'))
    alt = float(request.args.get('alt'))
    
    time = pd.DatetimeIndex([pd.to_datetime(time_str).tz_localize("America/New_York")])
    solpos = getSolarPosition(time, lat, lon, alt)
    
    return jsonify({
        'zenith': float(solpos['zenith'].values[0]),
        'azimuth': float(solpos['azimuth'].values[0])
    })

if __name__ == '__main__':
    app.run(port=5000)