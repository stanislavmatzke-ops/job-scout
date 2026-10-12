# Copy to infra/local.env.ps1 (ignored by git) and fill in. Nothing here is a secret except what you choose to keep here.
$env:JOBSCOUT_GOOGLE_CLIENT_ID    = '<Google client ID>'
$env:JOBSCOUT_MICROSOFT_CLIENT_ID = '<Microsoft client ID>'
$env:JOBSCOUT_ADMINS              = 'microsoft:<your subject>'      # GET /api/me → identity
$env:JOBSCOUT_ADMIN_EMAIL         = '<your e-mail for access requests>'
$env:JOBSCOUT_EMAIL_VERIFIED      = 'false'                          # 'true' once the e-mail domain is verified
# $env:JOBSCOUT_PG_PASSWORD       = '...'                            # optional – otherwise the script asks
