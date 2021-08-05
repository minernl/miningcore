from web3 import Web3
from io import StringIO
import argparse
import sys
from getpass import getpass

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('-s', '--source-addr', dest='sourceAddr', help='source wallet address', type=str, required=True)
    parser.add_argument('-d', '--dest-addr', dest='destAddr', help='destination wallet address', type=str, required=True)
    parser.add_argument('-a', '--amount', dest='amount', help='amount of transfer in wei', type=int, required=True)
    parser.add_argument('-g', '--gas-price', dest='gasPrice', help='gas price for transaction', type=int, default=-1, required=False)
    parser.add_argument('-n', '--nonce', dest='nonce', help='nonce for transcation. Use to overwrite/cancel existing transactions', type=int, default=-1, required=False)
    parser.add_argument('--host', dest='host', help='URL for ethereum node (default: http://localhost:8545)', type=str, default='http://localhost:8545', required=False)

    args = parser.parse_args()

    pKey = getpass("Enter private key for source wallet:")

    if not Web3.isAddress(args.sourceAddr):
        print(f'Destination address {args.sourceAddr} is invalid')
        exit(1)

    if not Web3.isAddress(args.destAddr):
        print(f'Destination address {args.destAddr} is invalid')
        exit(1)

    web3 = Web3(Web3.HTTPProvider(args.host))
    if not web3.isConnected():
        print(f'Failed to connect to ethereum host: {args.host}')
        exit(1)

    if args.nonce == -1:
        args.nonce = web3.eth.get_transaction_count(args.sourceAddr)

    if args.gasPrice == -1:
        args.gasPrice = web3.eth.gas_price

    chainId = web3.eth.chain_id

    transaction = {
        'to': args.destAddr,
        'value': args.amount,
        'gas': 21000,
        'gasPrice': args.gasPrice,
        'nonce': args.nonce,
        'chainId': chainId
    }

    print(transaction)

    signed = web3.eth.account.sign_transaction(transaction, pKey)
    txhash = web3.eth.send_raw_transaction(signed.rawTransaction)
    print(f'Transaction sent: {txhash}')

if __name__ == "__main__":
    main()
